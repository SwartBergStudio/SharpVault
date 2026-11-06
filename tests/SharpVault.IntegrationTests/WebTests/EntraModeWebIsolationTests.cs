// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Hosting;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class EntraModeWebIsolationTests(WebTestFactory factory)
{
    private const string PickerPath = "/account/local-sign-in";
    private const string SessionCookie = "__Host-SharpVault";
    private const string EntraRequired = "AzureAd:TenantId and AzureAd:ClientId are required outside the Development environment.";
    private const string ClientIdRequired = "AzureAd:ClientId is required when AzureAd:TenantId is set.";

    [Fact]
    public void WebTestFactory_BuiltHost_IsInEntraMode()
    {
        EntraModeAssertion.Assert(factory.Services);
    }

    [Fact]
    public async Task Picker_Get_And_Post_InEntraMode_Return404AndSetNoCookie()
    {
        using var client = factory.CreateWebClient();

        var get = await client.GetAsync(PickerPath, TestContext.Current.CancellationToken);
        var post = await client.PostAsync(
            PickerPath,
            new FormUrlEncodedContent(new Dictionary<string, string> { ["accountId"] = "5a1d0000-0000-4000-8000-000000000001" }),
            TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, post.StatusCode);
        Assert.DoesNotContain(SetCookies(get), value => value.StartsWith(SessionCookie, StringComparison.Ordinal));
        Assert.DoesNotContain(SetCookies(post), value => value.StartsWith(SessionCookie, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EntraModeWeb_CookieMintedInLocalMode_IsAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var local = new LocalWebTestFactory();
        var cookie = await LocalPickerSession.MintSessionCookieAsync(local, LocalAccounts.AdminOne, cancellationToken);
        using var client = factory.CreateWebClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/account");
        request.Headers.Add("Cookie", cookie);

        var response = await client.SendAsync(request, cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
    }

    [Fact]
    public void Startup_ProductionWithoutEntraSettings_FailsWithoutLocalFallback()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:TenantId", string.Empty);
            builder.UseSetting("AzureAd:ClientId", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EntraRequired, invalid.Message);
    }

    [Fact]
    public void Startup_ProductionWithOnlyTenantId_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("AzureAd:ClientId", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(ClientIdRequired, invalid.Message);
    }

    [Fact]
    public void Startup_StagingWithoutEntraSettings_Fails()
    {
        using var derived = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("AzureAd:TenantId", string.Empty);
            builder.UseSetting("AzureAd:ClientId", string.Empty);
        });

        var exception = Record.Exception(() => derived.CreateClient());

        var invalid = Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(EntraRequired, invalid.Message);
    }

    private static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? [.. values] : [];
}
