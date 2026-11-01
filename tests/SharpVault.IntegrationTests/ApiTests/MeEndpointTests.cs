// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpVault.Application.Features.Users.Dtos;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Models;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// GET /api/me: the signed-in user's own identity, derived from the validated token only.
/// </summary>
[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class MeEndpointTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private static readonly string[] ExpectedProperties = ["displayName", "isAdmin", "userId"];

    [Fact]
    public async Task GetMe_User_ReturnsOwnIdNameAndNotAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new CurrentUserDto(TestUsers.User.ObjectId, TestUsers.User.DisplayName, false), profile);
    }

    [Fact]
    public async Task GetMe_Admin_ReturnsAdminFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(MePath, cancellationToken);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.Equal(new CurrentUserDto(TestUsers.Admin.ObjectId, TestUsers.Admin.DisplayName, true), profile);
    }

    [Fact]
    public async Task GetMe_UserWithBothRoles_IsAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var both = TestUsers.User with { Roles = ["SharpVault.User", "SharpVault.Admin"] };
        SetAuthenticatedClient(both);

        var response = await Client.GetAsync(MePath, cancellationToken);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.True(profile!.IsAdmin);
    }

    [Fact]
    public async Task GetMe_Response_HasExactlyIdNameAndAdminFlag()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(MePath, cancellationToken);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(ExpectedProperties, document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetMe_Response_ContainsNoEmailTenantOrTokenData()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.Create(TestUsers.User);

        var response = await GetWithTokenAsync(MePath, token);

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        AssertUnauthorizedOrForbiddenBodyIsClean(
            body,
            TestUsers.User.Email!,
            TestTenant.Id.ToString("D"),
            TestApi.ClientId.ToString("D"),
            "access_as_user",
            "SharpVault.User",
            token);
    }

    [Fact]
    public async Task GetMe_DisplayNameClaimMissing_ReturnsEmptyName()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.For(TestUsers.User).WithoutClaim("name").Build();

        var response = await GetWithTokenAsync(MePath, token);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(string.Empty, profile!.DisplayName);
    }

    [Fact]
    public async Task GetMe_ControlCharactersInName_AreRemovedFromTheResponse()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = TestTokens.For(TestUsers.User).WithClaim("name", "Ada\u0000 Love‮lace\r\n").Build();

        var response = await GetWithTokenAsync(MePath, token);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(profile!.DisplayName, char.IsControl);
    }

    [Fact]
    public async Task GetMe_IdentityHintsInQueryAndHeaders_AreIgnored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var other = TestUsers.Admin.ObjectId.ToString("D");
        var request = new HttpRequestMessage(HttpMethod.Get, $"{MePath}?userId={other}&oid={other}&isAdmin=true&id={other}");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        request.Headers.Add("X-User-Id", other);
        request.Headers.Add("X-Oid", other);
        request.Headers.Add("X-Is-Admin", "true");
        request.Content = JsonContent.Create(new { userId = other, isAdmin = true });

        var response = await CreateUnauthenticatedClient().SendAsync(request, cancellationToken);

        var profile = await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TestUsers.User.ObjectId, profile!.UserId);
        Assert.False(profile.IsAdmin);
        Assert.Null(await UserRows.GetAsync(GetUsersTable(), TestUsers.Admin.ObjectId, cancellationToken));
    }

    [Fact]
    public async Task GetMe_TwoUsers_NeverSeeEachOther()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var users = new[] { TestUsers.User, TestUsers.OtherUser, TestUsers.Admin };

        var results = await Task.WhenAll(users.Select(async user =>
        {
            using var client = Factory.CreateAuthenticatedClient(user);
            var response = await client.GetAsync(MePath, cancellationToken);
            return (user, profile: await response.Content.ReadFromJsonAsync<CurrentUserDto>(cancellationToken));
        }));

        foreach (var (user, profile) in results)
        {
            Assert.Equal(user.ObjectId, profile!.UserId);
            Assert.Equal(user.DisplayName, profile.DisplayName);
        }
    }

    [Fact]
    public async Task GetMe_Response_SendsNoStoreAndApiSecurityHeaders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var response = await Client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.OrdinalIgnoreCase);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Theory]
    [InlineData(CallerKind.Anonymous)]
    [InlineData(CallerKind.AuthenticatedWithoutRole)]
    [InlineData(CallerKind.LockedUser)]
    public async Task GetMe_DeniedResponses_HaveSecurityHeadersAndNoStore(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await ArrangeCallerAsync(caller);

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.OrdinalIgnoreCase);
        SecurityHeaderAssertions.AssertApi(response);
    }

    [Fact]
    public async Task GetMe_HeadRequest_DoesNotBypassAuthorization()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var anonymous = await CreateUnauthenticatedClient().SendAsync("HEAD", MePath, cancellationToken);
        using var noRole = Factory.CreateAuthenticatedClient(TestUsers.AuthenticatedWithoutRole);
        var forbidden = await noRole.SendAsync("HEAD", MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(0, StorageRequests.Count);
    }

    [Fact]
    public async Task GetMe_UserCalledTwice_ReturnsTheSameProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.User);

        var first = await Client.GetFromJsonAsync<CurrentUserDto>(MePath, cancellationToken);
        var second = await Client.GetFromJsonAsync<CurrentUserDto>(MePath, cancellationToken);

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ApiContract_GetMe_DeserializesIntoUiSharedProfile()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        SetAuthenticatedClient(TestUsers.Admin);

        var response = await Client.GetAsync(MePath, cancellationToken);

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var dto = JsonSerializer.Deserialize<CurrentUserDto>(json, JsonSerializerOptions.Web);
        var profile = JsonSerializer.Deserialize<CurrentUserProfile>(json, JsonSerializerOptions.Web);
        Assert.NotNull(dto);
        Assert.NotNull(profile);
        Assert.Equal(dto.UserId, profile.UserId);
        Assert.Equal(dto.DisplayName, profile.DisplayName);
        Assert.Equal(dto.IsAdmin, profile.IsAdmin);
        Assert.Equal(
            typeof(CurrentUserDto).GetProperties().Where(property => property.Name != "EqualityContract").Select(Describe).Order(StringComparer.Ordinal),
            typeof(CurrentUserProfile).GetProperties().Where(property => property.Name != "EqualityContract").Select(Describe).Order(StringComparer.Ordinal));
    }

    private static string Describe(System.Reflection.PropertyInfo property) => $"{property.Name}:{property.PropertyType.FullName}";
}
