// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// TEN-3, TEN-4, VIS-2 and the no-enumeration rule: who is allowed in, and that every denial looks the same.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class AccessControlTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const string UserRole = "SharpVault.User";
    private const string AdminRole = "SharpVault.Admin";
    private const string UriRoleClaim = "http://schemas.microsoft.com/ws/2008/06/identity/claims/role";
    private const string GlobalAdministratorWid = "62e90394-69f5-4237-9190-012177145e10";
    private const string Disclosing = "lock|guest|role|tenant|disabled|inactive|suspend";
    private const int OversizedNameLength = 10_000;
    private const int MaximumNameLength = 256;

    private static readonly int[] MemberAndGuestAccountTypes = [0, 1];

    public static TheoryData<string> InjectedRoleScenarios() =>
    [
        "RoleClaim",
        "RoleArrayClaim",
        "WidsClaim",
        "GroupsClaim",
        "ScopeClaim",
        "UriRoleClaim",
        "LowercaseRole",
        "UppercaseRole",
        "PaddedRole",
        "TrailingSpaceRole",
        "NulSuffixRole",
        "CommaJoinedRoles",
        "SpaceJoinedRoles",
        "CapitalizedRolesClaim",
        "NestedRolesObject",
        "EmptyRolesArray",
        "NullRoles",
    ];

    public static TheoryData<string> GuestScenarios() =>
    [
        "AcctOne",
        "ForeignIdp",
        "ForeignIdpV2Issuer",
        "AcctMissing",
        "AcctTwo",
        "AcctEmptyString",
        "AcctArray",
        "AcctText",
    ];

    public static TheoryData<string> NonActiveStatuses() =>
    [
        "Disabled",
        "active",
        "ACTIVE",
        " Active",
        "Active ",
        string.Empty,
        "Locked",
        "1",
        "true",
        "Admin",
        "Active\u0000",
        "Activé",
    ];

    public static TheoryData<string> AdminQueryAbuse() =>
    [
        "continuationToken=5f0c1d2e-4b7a-4c3d-9e8f-00000000b002",
        "continuationToken=garbage",
        "continuationToken=%27%20or%20PartitionKey%20ne%20%27",
        "continuationToken=",
        "pageSize=0",
        "pageSize=-1",
        "pageSize=abc",
        "pageSize=101&continuationToken=x",
        "pageSize=1&pageSize=2",
    ];

    [Theory]
    [MemberData(nameof(GuestScenarios))]
    public async Task ApiRequest_GuestToken_Returns403(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var builder = TestTokens.For(TestUsers.User);
        _ = scenario switch
        {
            "AcctOne" => builder.WithClaim("acct", 1),
            "ForeignIdp" => builder.WithClaim("idp", $"https://sts.windows.net/{TestTenant.OtherId:D}/"),
            "ForeignIdpV2Issuer" => builder.WithClaim("idp", TestIdentityProvider.Issuer(TestTenant.OtherId)),
            "AcctMissing" => builder.WithoutClaim("acct"),
            "AcctTwo" => builder.WithClaim("acct", 2),
            "AcctEmptyString" => builder.WithClaim("acct", string.Empty),
            "AcctArray" => builder.WithClaim("acct", MemberAndGuestAccountTypes),
            "AcctText" => builder.WithClaim("acct", "member"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var response = await GetWithTokenAsync(MePath, builder.Build());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Theory]
    [InlineData("OwnV1Issuer")]
    [InlineData("OwnV2Issuer")]
    [InlineData("NoIdp")]
    public async Task ApiRequest_MemberWithOwnTenantIdentityProvider_Returns200(string scenario)
    {
        var builder = TestTokens.For(TestUsers.User);
        _ = scenario switch
        {
            "OwnV1Issuer" => builder.WithClaim("idp", $"https://sts.windows.net/{TestTenant.Id:D}/"),
            "OwnV2Issuer" => builder.WithClaim("idp", TestIdentityProvider.Issuer(TestTenant.Id)),
            "NoIdp" => builder.WithoutClaim("idp"),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
        };

        var response = await GetWithTokenAsync(MePath, builder.Build());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ApiRequest_GuestAdminToken_ForAdminEndpoint_Returns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.For(TestUsers.Admin).WithClaim("acct", 1).Build();

        var response = await GetWithTokenAsync(AdminUsersPath, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Fact]
    public async Task ApiRequest_GuestTokenWhenGuestsAllowed_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var allowing = Factory.WithWebHostBuilder(builder => builder.UseSetting("Tenancy:AllowGuests", "true"));
        using var client = CallerClients.CreateFor(allowing, CallerKind.Guest);

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await UserRows.GetAsync(GetUsersTable(), TestUsers.Guest.ObjectId, cancellationToken));
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ApiRequest_GuestTokenWhenGuestsAllowedButNoRole_Returns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var allowing = Factory.WithWebHostBuilder(builder => builder.UseSetting("Tenancy:AllowGuests", "true"));
        var token = TestTokens.Create(TestUsers.Guest with { Roles = [] });
        using var client = CreateClientFor(allowing, token);
        StorageRequests.Clear();

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task ApiRequest_GuestTokenWhenGuestsAllowedButLocked_Returns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.Guest, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var allowing = Factory.WithWebHostBuilder(builder => builder.UseSetting("Tenancy:AllowGuests", "true"));
        using var client = CallerClients.CreateFor(allowing, CallerKind.Guest);

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task ApiRequest_GuestAdminWhenGuestsAllowed_StillNeedsTheAdminRoleForAdminEndpoint()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var allowing = Factory.WithWebHostBuilder(builder => builder.UseSetting("Tenancy:AllowGuests", "true"));
        using var client = CreateClientFor(allowing, TestTokens.Create(TestUsers.Guest));

        var response = await client.GetAsync(AdminUsersPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InjectedRoleScenarios))]
    public async Task AdminEndpoint_RoleInjectedThroughOtherClaim_Returns403(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = InjectRole(scenario, AdminRole).Build();

        var response = await GetWithTokenAsync(AdminUsersPath, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Theory]
    [MemberData(nameof(InjectedRoleScenarios))]
    public async Task UserEndpoint_RoleInjectedThroughOtherClaim_Returns403(string scenario)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = InjectRole(scenario, UserRole).Build();

        var response = await GetWithTokenAsync(MePath, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
        Assert.Equal(0, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Theory]
    [MemberData(nameof(InjectedRoleScenarios))]
    public async Task UserRoleToken_WithInjectedAdminClaim_CannotReachAdminEndpoint(string scenario)
    {
        var token = Inject(TestTokens.For(TestUsers.User), scenario, AdminRole).Build();

        var response = await GetWithTokenAsync(AdminUsersPath, token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_TestHeadersWithoutToken_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, path);
            SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

            var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData(MePath)]
    [InlineData(AdminUsersPath)]
    public async Task ApiRequest_TestRoleHeaderWithNoRoleToken_Returns403(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.AuthenticatedWithoutRole));
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task ApiRequest_TestHeadersWithUserToken_DoNotChangeIdentityOrRole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var meRequest = new HttpRequestMessage(HttpMethod.Get, MePath);
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        SpoofedTestHeaders.Add(meRequest.Headers, TestUsers.Admin);
        var adminRequest = new HttpRequestMessage(HttpMethod.Get, AdminUsersPath);
        adminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        SpoofedTestHeaders.Add(adminRequest.Headers, TestUsers.Admin);
        var client = CreateUnauthenticatedClient();

        var me = await client.SendAsync(meRequest, cancellationToken);
        var admin = await client.SendAsync(adminRequest, cancellationToken);

        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(TestUsers.User.ObjectId, body.RootElement.GetProperty("userId").GetGuid());
        Assert.False(body.RootElement.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, admin.StatusCode);
        Assert.Null(await UserRows.GetAsync(GetUsersTable(), TestUsers.Admin.ObjectId, cancellationToken));
    }

    [Fact]
    public async Task ForbiddenResponses_LockedNoRoleAndGuest_AreIdentical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shapes = new List<ErrorResponseShape>();
        foreach (var caller in new[] { CallerKind.LockedUser, CallerKind.AuthenticatedWithoutRole, CallerKind.Guest })
        {
            using var client = await ArrangeCallerAsync(caller);
            var response = await client.GetAsync(MePath, cancellationToken);
            shapes.Add(await ErrorResponseShape.CaptureAsync(response, cancellationToken));
        }

        Assert.All(shapes, shape => Assert.Equal(HttpStatusCode.Forbidden, shape.Status));
        Assert.All(shapes, shape => Assert.Equal(ProblemJsonMediaType, shape.ContentType));
        Assert.Single(shapes.Distinct());
    }

    [Fact]
    public async Task ForbiddenResponses_AdminEndpointDenialReasons_AreIdentical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var shapes = new List<ErrorResponseShape>();
        foreach (var caller in new[] { CallerKind.User, CallerKind.LockedAdmin, CallerKind.LockedUser, CallerKind.AuthenticatedWithoutRole, CallerKind.Guest })
        {
            using var client = await ArrangeCallerAsync(caller);
            var response = await client.GetAsync(AdminUsersPath, cancellationToken);
            shapes.Add(await ErrorResponseShape.CaptureAsync(response, cancellationToken));
        }

        Assert.All(shapes, shape => Assert.Equal(HttpStatusCode.Forbidden, shape.Status));
        Assert.Single(shapes.Distinct());
    }

    [Fact]
    public async Task ForbiddenResponses_AcrossEndpoints_AreIdentical()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var locked = await ArrangeCallerAsync(CallerKind.LockedUser);
        using var noRole = await ArrangeCallerAsync(CallerKind.AuthenticatedWithoutRole);
        var shapes = new List<ErrorResponseShape>();
        foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
        {
            shapes.Add(await ErrorResponseShape.CaptureAsync(await locked.GetAsync(path, cancellationToken), cancellationToken));
            shapes.Add(await ErrorResponseShape.CaptureAsync(await noRole.GetAsync(path, cancellationToken), cancellationToken));
        }

        Assert.Single(shapes.Distinct());
    }

    [Fact]
    public async Task ForbiddenResponses_Body_DoesNotRevealTheDenialReason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var caller in new[] { CallerKind.LockedUser, CallerKind.AuthenticatedWithoutRole, CallerKind.Guest })
        {
            using var client = await ArrangeCallerAsync(caller);
            var user = CallerClients.UserFor(caller)!;

            var response = await client.GetAsync(MePath, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var headers = response.Headers.ToString();
            foreach (var fragment in Disclosing.Split('|'))
            {
                Assert.DoesNotContain(fragment, body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(fragment, headers, StringComparison.OrdinalIgnoreCase);
            }

            AssertUnauthorizedOrForbiddenBodyIsClean(body, user.ObjectId.ToString("D"), user.DisplayName);
            if (user.Email is not null)
            {
                AssertUnauthorizedOrForbiddenBodyIsClean(body, user.Email);
            }
        }
    }

    [Theory]
    [MemberData(nameof(AdminQueryAbuse))]
    public async Task AdminUsers_NonAdminWithAnyQuery_Returns403BeforeValidation(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var caller in new[] { CallerKind.User, CallerKind.AuthenticatedWithoutRole, CallerKind.LockedUser })
        {
            using var client = await ArrangeCallerAsync(caller);
            StorageRequests.Clear();

            var response = await client.GetAsync($"{AdminUsersPath}?{query}", cancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.DoesNotContain(StorageRequests.Requests, request => request.IsEntityQuery);
        }
    }

    [Theory]
    [MemberData(nameof(AdminQueryAbuse))]
    public async Task AdminUsers_AnonymousWithAnyQuery_Returns401(string query)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await CreateUnauthenticatedClient().GetAsync($"{AdminUsersPath}?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData("/api/admin/users/")]
    [InlineData("/API/ADMIN/USERS")]
    [InlineData("/Api/Admin/Users/")]
    [InlineData("/api/admin/users/.")]
    [InlineData("/api//admin/users")]
    [InlineData("/api/admin//users")]
    [InlineData("/api/%61dmin/users")]
    [InlineData("/api/admin/users%2f")]
    [InlineData("/api/me/../admin/users")]
    [InlineData("/api/admin/users;x=1")]
    [InlineData("/api/admin/users.json")]
    [InlineData("/api/admin/users/00000000-0000-0000-0000-000000000000")]
    public async Task AdminUsersRouteVariants_UserRoleCaller_NeverSucceeds(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(path, cancellationToken);

        Assert.True(
            response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound,
            $"Unexpected status {response.StatusCode} for {path}.");
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("\"users\"", body, StringComparison.OrdinalIgnoreCase);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [InlineData("/api/admin/users/")]
    [InlineData("/API/ADMIN/USERS")]
    [InlineData("/api//admin/users")]
    [InlineData("/api/%61dmin/users")]
    [InlineData("/api/me/")]
    [InlineData("/API/ME")]
    [InlineData("/api/%6De")]
    public async Task ApiRouteVariants_Anonymous_Returns401(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await CreateUnauthenticatedClient().GetAsync(path, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task UnsupportedMethods_OnProtectedEndpoints_AreDeniedBeforeAnythingElse(string method)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var path in new[] { MePath, AdminUsersPath })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await CreateUnauthenticatedClient().SendAsync(method, path, cancellationToken)).StatusCode);

            using var noRole = Factory.CreateAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
            Assert.Equal(HttpStatusCode.Forbidden, (await noRole.SendAsync(method, path, cancellationToken)).StatusCode);
        }

        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task ApiRequest_UsersTableUnavailable_FailsClosedWithoutDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var broken = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserStore>();
            services.AddSingleton<IUserStore, ThrowingUserStore>();
        }));
        foreach (var user in new[] { TestUsers.User, TestUsers.Admin })
        {
            using var client = CreateClientFor(broken, TestTokens.Create(user));

            foreach (var path in new[] { MePath, AdminUsersPath, UnknownPath })
            {
                var response = await client.GetAsync(path, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (user == TestUsers.User && path == AdminUsersPath)
                {
                    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
                    continue;
                }

                Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
                Assert.Equal(ProblemJsonMediaType, response.Content.Headers.ContentType?.MediaType);
                AssertUnauthorizedOrForbiddenBodyIsClean(
                    body,
                    "RequestFailedException",
                    "Service unavailable",
                    "Users",
                    "   at ",
                    "Azure",
                    "stackTrace",
                    "exception",
                    user.DisplayName);
            }
        }
    }

    [Fact]
    public async Task ApiRequest_UsersTableUnavailable_DoesNotAffectDeniedOrAnonymousCallers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var broken = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IUserStore>();
            services.AddSingleton<IUserStore, ThrowingUserStore>();
        }));

        using var anonymous = CreateClientFor(broken);
        using var noRole = CreateClientFor(broken, TestTokens.Create(TestUsers.AuthenticatedWithoutRole));
        using var guest = CreateClientFor(broken, TestTokens.Create(TestUsers.Guest));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(MePath, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRole.GetAsync(MePath, cancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync(MePath, cancellationToken)).StatusCode);
    }

    [Fact]
    public async Task ApiRequest_AuditWriteFails_FailsClosedWithoutDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var broken = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAuditLog>();
            services.AddSingleton<IAuditLog, ThrowingAuditLog>();
        }));
        using var client = CreateClientFor(broken, TestTokens.Create(TestUsers.User));

        var response = await client.GetAsync(MePath, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        AssertUnauthorizedOrForbiddenBodyIsClean(body, "RequestFailedException", "Service unavailable", "AuditLog", "   at ", TestUsers.User.DisplayName);
    }

    [Fact]
    public async Task GetMe_RowWithMissingStatusColumn_Returns403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.DisplayName] = "Row without status",
            [UserColumns.CreatedAt] = DateTimeOffset.UtcNow,
            [UserColumns.LastSignInAt] = DateTimeOffset.UtcNow,
        });
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await ListAuditRowsAsync());
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [MemberData(nameof(NonActiveStatuses))]
    public async Task GetMe_RowWithUnknownStatus_Returns403(string status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.DisplayName] = TestUsers.User.DisplayName,
            [UserColumns.Status] = status,
            [UserColumns.CreatedAt] = DateTimeOffset.UtcNow.AddDays(-30),
            [UserColumns.LastSignInAt] = DateTimeOffset.UtcNow.AddDays(-30),
        });
        var before = await RequireUserRowAsync(TestUsers.User.ObjectId);
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var after = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.Equal(before.ETag, after.ETag);
        Assert.Empty(await ListAuditRowsAsync());
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task GetMe_RowWithNumericStatus_IsNeverTreatedAsActive()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.DisplayName] = TestUsers.User.DisplayName,
            [UserColumns.Status] = 1,
            [UserColumns.CreatedAt] = DateTimeOffset.UtcNow.AddDays(-30),
            [UserColumns.LastSignInAt] = DateTimeOffset.UtcNow.AddDays(-30),
        });
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ListAuditRowsAsync());
    }

    [Fact]
    public async Task GetMe_ActiveRowMissingOptionalColumns_Returns200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await InsertRawUserRowAsync(TestUsers.User.ObjectId, new Dictionary<string, object?>
        {
            [UserColumns.Status] = UserRows.ActiveStatus,
        });
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task ApiRequest_TwoUsersWithSameEmail_AreSeparateUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string shared = "shared@sharpvault.test";
        var first = TestUsers.User with { Email = shared };
        var second = TestUsers.OtherUser with { Email = shared };

        var firstResponse = await Factory.CreateAuthenticatedClient(first).GetAsync(MePath, cancellationToken);
        var secondResponse = await Factory.CreateAuthenticatedClient(second).GetAsync(MePath, cancellationToken);

        using var firstBody = JsonDocument.Parse(await firstResponse.Content.ReadAsStringAsync(cancellationToken));
        using var secondBody = JsonDocument.Parse(await secondResponse.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(first.ObjectId, firstBody.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(second.ObjectId, secondBody.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(2, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Fact]
    public async Task ApiRequest_LockedUserEmailClaimedByAnotherAccount_DoesNotLockOrUnlockThatAccount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await Factory.SeedUserAsync(TestUsers.LockedUser, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        var impersonator = TestUsers.User with { Email = TestUsers.LockedUser.Email, DisplayName = TestUsers.LockedUser.DisplayName };

        var impersonatorResponse = await Factory.CreateAuthenticatedClient(impersonator).GetAsync(MePath, cancellationToken);
        var lockedResponse = await Factory.CreateAuthenticatedClient(TestUsers.LockedUser).GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, impersonatorResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, lockedResponse.StatusCode);
        var lockedRow = await RequireUserRowAsync(TestUsers.LockedUser.ObjectId);
        Assert.Equal(UserRows.LockedStatus, lockedRow.GetString(UserColumns.Status));
    }

    [Fact]
    public async Task ApiRequest_IdentityClaimsOfAnotherUser_DoNotChangeIdentity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = TestUsers.Admin;
        var token = TestTokens.For(TestUsers.User)
            .WithClaim("sub", admin.ObjectId.ToString("D"))
            .WithClaim("upn", admin.Email!)
            .WithClaim("unique_name", admin.Email!)
            .WithClaim("email", admin.Email!)
            .WithClaim("preferred_username", admin.Email!)
            .WithClaim("name", admin.DisplayName)
            .WithClaim("azp", TestApi.ClientId.ToString("D"))
            .WithClaim("appid", Guid.NewGuid().ToString("D"))
            .Build();

        var me = await GetWithTokenAsync(MePath, token);
        var list = await GetWithTokenAsync(AdminUsersPath, token);

        using var body = JsonDocument.Parse(await me.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.Equal(TestUsers.User.ObjectId, body.RootElement.GetProperty("userId").GetGuid());
        Assert.False(body.RootElement.GetProperty("isAdmin").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Null(await UserRows.GetAsync(GetUsersTable(), admin.ObjectId, cancellationToken));
    }

    [Fact]
    public async Task ApiRequest_UppercaseObjectId_ResolvesToTheSameUserRow()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var upper = TestUsers.User.ObjectId.ToString("D").ToUpperInvariant();
        var lowerToken = TestTokens.Create(TestUsers.User);
        var upperToken = TestTokens.For(TestUsers.User).WithClaim("oid", upper).Build();

        var first = await GetWithTokenAsync(MePath, lowerToken);
        var second = await GetWithTokenAsync(MePath, upperToken);

        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(TestUsers.User.ObjectId, body.RootElement.GetProperty("userId").GetGuid());
        Assert.Equal(1, await UserRows.CountAsync(GetUsersTable(), cancellationToken));
    }

    [Fact]
    public async Task ApiRequest_OversizedDisplayName_StoredTruncated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var hostile = "Ab\u0007c\r\n\u001b[31m" + new string('x', OversizedNameLength);
        var user = TestUsers.User with { DisplayName = hostile };
        SetAuthenticatedClient(user);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(user.ObjectId);
        var stored = row.GetString(UserColumns.DisplayName);
        Assert.Equal(MaximumNameLength, stored.Length);
        Assert.DoesNotContain(stored, char.IsControl);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(stored, body.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task ApiRequest_HostileDisplayNameAndEmail_AreStoredAsDataNotInterpreted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = TestUsers.User with
        {
            DisplayName = "<script>alert(1)</script>' or PartitionKey ne '",
            Email = "o'brien+tag@sharpvault.test",
        };
        SetAuthenticatedClient(user);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(user.ObjectId);
        Assert.Equal(user.DisplayName, row.GetString(UserColumns.DisplayName));
        Assert.Equal(user.Email, row.GetString(UserColumns.Email));
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        AssertNoQueryWithoutPartitionKey();
    }

    [Theory]
    [InlineData("no-at-sign")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ApiRequest_InvalidEmailClaim_StoresNoEmail(string email)
    {
        var token = TestTokens.For(TestUsers.User).WithClaim("email", email).WithClaim("preferred_username", email).Build();

        var response = await GetWithTokenAsync(MePath, token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = await RequireUserRowAsync(TestUsers.User.ObjectId);
        Assert.True(string.IsNullOrEmpty(row.GetString(UserColumns.Email)));
    }

    private static TestTokenBuilder InjectRole(string scenario, string role) =>
        Inject(TestTokens.For(TestUsers.AuthenticatedWithoutRole), scenario, role);

    private static TestTokenBuilder Inject(TestTokenBuilder builder, string scenario, string role) => scenario switch
    {
        "RoleClaim" => builder.WithClaim("role", role),
        "RoleArrayClaim" => builder.WithClaim("role", new[] { role }),
        "WidsClaim" => builder.WithClaim("wids", new[] { GlobalAdministratorWid }),
        "GroupsClaim" => builder.WithClaim("groups", new[] { role }),
        "ScopeClaim" => builder.WithClaim("scp", $"{TestApi.Scope} {role}"),
        "UriRoleClaim" => builder.WithClaim(UriRoleClaim, role),
        "LowercaseRole" => builder.WithClaim("roles", new[] { role.ToLowerInvariant() }),
        "UppercaseRole" => builder.WithClaim("roles", new[] { role.ToUpperInvariant() }),
        "PaddedRole" => builder.WithClaim("roles", new[] { " " + role }),
        "TrailingSpaceRole" => builder.WithClaim("roles", new[] { role + " " }),
        "NulSuffixRole" => builder.WithClaim("roles", new[] { role + "\u0000" }),
        "CommaJoinedRoles" => builder.WithClaim("roles", $"SharpVault.Other,{role}"),
        "SpaceJoinedRoles" => builder.WithClaim("roles", $"SharpVault.Other {role}"),
        "CapitalizedRolesClaim" => builder.WithClaim("Roles", new[] { role }),
        "NestedRolesObject" => builder.WithClaim("roles", new Dictionary<string, string> { ["value"] = role }),
        "EmptyRolesArray" => builder.WithClaim("roles", Array.Empty<string>()),
        "NullRoles" => builder.WithClaim("roles", string.Empty),
        _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null),
    };
}
