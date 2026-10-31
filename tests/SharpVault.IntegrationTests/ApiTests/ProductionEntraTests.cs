// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests;

/// <summary>
/// TEN-1 to TEN-4 in the Production environment. Production cannot reach storage, so only outcomes decided before the access gate are covered.
/// </summary>
[Collection(nameof(ProductionTestGroup))]
[Trait("Category", "Security")]
public sealed class ProductionEntraTests(ProductionApiTestFactory factory)
{
    private const string TenantIdInvalidMessage = "AzureAd:TenantId must be the directory (tenant) id GUID. Multi-tenant values such as common, organizations and consumers are not allowed.";
    private const string ClientIdInvalidMessage = "AzureAd:ClientId must be the application (client) id GUID.";
    private const string InstanceInvalidMessage = "AzureAd:Instance must be an absolute https URI with no path, query or user information.";
    private const string MePath = "/api/me";
    private const string AdminUsersPath = "/api/admin/users";

    public static TheoryData<CallerKind, string, HttpStatusCode> PreStorageMatrix()
    {
        var data = new TheoryData<CallerKind, string, HttpStatusCode>();
        var rows = new (CallerKind Caller, HttpStatusCode Expected)[]
        {
            (CallerKind.Anonymous, HttpStatusCode.Unauthorized),
            (CallerKind.AlgNoneToken, HttpStatusCode.Unauthorized),
            (CallerKind.UnknownKeyToken, HttpStatusCode.Unauthorized),
            (CallerKind.OtherTenantUser, HttpStatusCode.Unauthorized),
            (CallerKind.Guest, HttpStatusCode.Forbidden),
            (CallerKind.AuthenticatedWithoutRole, HttpStatusCode.Forbidden),
        };
        foreach (var row in rows)
        {
            data.Add(row.Caller, MePath, row.Expected);
            data.Add(row.Caller, AdminUsersPath, row.Expected);
        }

        return data;
    }

    public static TheoryData<string, string, string> InvalidSettings() => new()
    {
        { "AzureAd:TenantId", "contoso-7f3c9a.onmicrosoft.com", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "common", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "organizations", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "consumers", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "00000000-0000-0000-0000-000000000000", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "{5f0c1d2e-4b7a-4c3d-9e8f-00000000a001}", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "5f0c1d2e4b7a4c3d9e8f00000000a001", TenantIdInvalidMessage },
        { "AzureAd:TenantId", "", TenantIdInvalidMessage },
        { "AzureAd:ClientId", "client-7f3c9a", ClientIdInvalidMessage },
        { "AzureAd:ClientId", "00000000-0000-0000-0000-000000000000", ClientIdInvalidMessage },
        { "AzureAd:ClientId", "", ClientIdInvalidMessage },
        { "AzureAd:Instance", "http://login.microsoftonline.com/", InstanceInvalidMessage },
        { "AzureAd:Instance", "https://login.microsoftonline.com/tenant-7f3c9a/", InstanceInvalidMessage },
        { "AzureAd:Instance", "https://user-7f3c9a:pw-7f3c9a@login.microsoftonline.com/", InstanceInvalidMessage },
        { "AzureAd:Instance", "https://login.microsoftonline.com/?x=7f3c9a", InstanceInvalidMessage },
        { "AzureAd:Instance", "login-7f3c9a.example", InstanceInvalidMessage },
        { "AzureAd:Instance", "", InstanceInvalidMessage },
    };

    [Theory]
    [MemberData(nameof(PreStorageMatrix))]
    public async Task AuthorizationMatrix_ProductionPreStorageCallers_ReturnExpectedStatus(
        CallerKind caller,
        string path,
        HttpStatusCode expected)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, caller);

        var response = await client.GetAsync(path, cancellationToken);

        Assert.Equal(expected, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Startup_InvalidEntraSettings_FailsWithoutEchoingValue(string key, string value, string expectedMessage)
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(expectedMessage, invalid.Message);
        if (value.Length > 8 && !expectedMessage.Contains(value, StringComparison.Ordinal))
        {
            Assert.DoesNotContain(value, invalid.ToString(), StringComparison.Ordinal);
            Assert.False(factory.Logs.ContainsText(value));
        }
    }

    [Theory]
    [InlineData("AzureAd:TenantId")]
    [InlineData("AzureAd:ClientId")]
    [InlineData("AzureAd:Instance")]
    public void Startup_MissingEntraSettings_Fails(string key)
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting(key, string.Empty));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Theory]
    [InlineData("AzureAd:TenantId", "common")]
    [InlineData("AzureAd:TenantId", "organizations")]
    [InlineData("AzureAd:TenantId", "consumers")]
    public void Startup_MultiTenantAuthority_NeverStartsServing(string key, string value)
    {
        using var derived = factory.WithWebHostBuilder(builder => builder.UseSetting(key, value));

        var exception = Record.Exception(() => derived.CreateClient());

        Assert.IsType<InvalidOperationException>(exception);
    }

    [Theory]
    [InlineData(CallerKind.Guest)]
    [InlineData(CallerKind.AuthenticatedWithoutRole)]
    public async Task ApiRequest_ProductionGuestOrNoRoleToken_Returns403(CallerKind caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, caller);

        var meResponse = await client.GetAsync(MePath, cancellationToken);
        var adminResponse = await client.GetAsync(AdminUsersPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, meResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, adminResponse.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_ProductionOtherTenantToken_Returns401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, CallerKind.OtherTenantUser);

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_ProductionValidTokenRejectedByTenantCheck_LogsReasonOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        factory.Logs.Clear();
        var foreignTenant = TestTenant.OtherId.ToString("D");
        var user = TestUsers.User;
        var token = TestTokens.For(user).WithClaim("tid", foreignTenant).Build();
        using var client = factory.CreateUnauthenticatedClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync(MePath, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var rejection = Assert.Single(factory.Logs.Entries, entry => entry.Message.Contains("WrongTenant", StringComparison.Ordinal));
        Assert.Equal(Microsoft.Extensions.Logging.LogLevel.Warning, rejection.Level);
        foreach (var value in new[] { foreignTenant, user.ObjectId.ToString("D"), user.DisplayName, user.Email!, token })
        {
            Assert.False(factory.Logs.ContainsText(value), "A log entry carried a claim value.");
        }
    }

    [Theory]
    [InlineData(MePath)]
    [InlineData(AdminUsersPath)]
    [InlineData("/does-not-exist")]
    public async Task ApiRequest_ProductionSpoofedIdentityHeadersOnly_Returns401(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = factory.CreateUnauthenticatedClient();
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-ID", TestUsers.Admin.ObjectId.ToString());
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL-NAME", TestUsers.Admin.Email!);
        request.Headers.Add("X-MS-CLIENT-PRINCIPAL", Convert.ToBase64String("""{"auth_typ":"aad","claims":[{"typ":"roles","val":"SharpVault.Admin"}]}"""u8.ToArray()));
        request.Headers.Add("X-Forwarded-User", TestUsers.Admin.Email!);
        request.Headers.Add("X-Forwarded-Email", TestUsers.Admin.Email!);
        request.Headers.Add("X-Remote-User", TestUsers.Admin.Email!);

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(MePath)]
    [InlineData(AdminUsersPath)]
    public async Task ApiRequest_ProductionSpoofedAdminHeadersWithNoRoleToken_Returns403(string path)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = CallerClients.CreateFor(factory, CallerKind.AuthenticatedWithoutRole);
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        SpoofedTestHeaders.Add(request.Headers, TestUsers.Admin);

        var response = await client.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ApiRequest_ProductionDenialResponses_AreIdenticalForGuestAndNoRole()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var guest = CallerClients.CreateFor(factory, CallerKind.Guest);
        using var noRole = CallerClients.CreateFor(factory, CallerKind.AuthenticatedWithoutRole);

        var guestShape = await ErrorResponseShape.CaptureAsync(await guest.GetAsync(MePath, cancellationToken), cancellationToken);
        var noRoleShape = await ErrorResponseShape.CaptureAsync(await noRole.GetAsync(MePath, cancellationToken), cancellationToken);

        Assert.Equal(guestShape, noRoleShape);
        Assert.Equal("application/problem+json", guestShape.ContentType);
    }

    [Fact]
    public async Task ApiRequest_ProductionRejectedBodies_ContainNoTenantOrClientIds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        foreach (var caller in new[] { CallerKind.Anonymous, CallerKind.OtherTenantUser, CallerKind.Guest, CallerKind.AuthenticatedWithoutRole })
        {
            using var client = CallerClients.CreateFor(factory, caller);
            var response = await client.GetAsync(MePath, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            using var document = JsonDocument.Parse(body);
            Assert.DoesNotContain(TestTenant.Id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TestTenant.OtherId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TestApi.ClientId.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("login.microsoftonline", body, StringComparison.OrdinalIgnoreCase);
        }
    }
}
