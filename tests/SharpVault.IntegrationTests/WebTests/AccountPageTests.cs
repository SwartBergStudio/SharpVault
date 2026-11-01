// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Web;
using NSubstitute;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.UI.Shared.Services;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebTestGroup))]
[Trait("Category", "Security")]
public sealed class AccountPageTests(WebTestFactory factory)
{
    private const string TokenAcquisitionMarker = "SV-SENSITIVE-ACQUISITION-9b21c4";

    [Fact]
    public async Task AccountPage_ApiReturnsProfile_RendersNameAndUserId()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userId = Guid.NewGuid();
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{userId}\",\"displayName\":\"Ada Lovelace\",\"isAdmin\":false}}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"account-section\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"account-heading\"", html, StringComparison.Ordinal);
        Assert.Matches("id=\"account-display-name\"[^>]*>Ada Lovelace<", html);
        Assert.Matches($"id=\"account-user-id\"[^>]*>{userId}<", html);
        Assert.DoesNotContain("id=\"account-admin-badge\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-error\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_ApiReturnsAdminProfile_RendersAdminBadge()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"Admin\",\"isAdmin\":true}}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.Admin, cancellationToken);

        var html = await client.GetStringAsync("/account", cancellationToken);

        Assert.Contains("id=\"account-admin-badge\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_ApiReturnsMarkupInDisplayName_EncodesIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        const string name = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"{name}\",\"isAdmin\":false}}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var html = await client.GetStringAsync("/account", cancellationToken);

        Assert.DoesNotContain("<img src=x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(2)", html, StringComparison.Ordinal);
        Assert.Contains("&lt;img src=x", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_SignedIn_CallsOnlyTheConfiguredApiWithBearerTokenAndRelativePath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stub = StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"Name\",\"isAdmin\":false}}");
        using var host = CreateHost(stub);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        await client.GetAsync("/account", cancellationToken);

        var request = Assert.Single(stub.Requests);
        Assert.Equal("localhost", request.Uri.Host);
        Assert.Equal("/api/me", request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Query);
        Assert.Equal("Bearer", request.Authorization?.Scheme);
        Assert.Equal(TestTokens.ForObjectId(TestUsers.User.ObjectId).Split('.')[0], request.Authorization?.Parameter?.Split('.')[0]);
        Assert.Equal(HttpMethod.Get, request.Method);
    }

    [Fact]
    public async Task AccountPage_ApiUnreachable_ShowsErrorMessageWithoutFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Throwing(new HttpRequestException("Connection refused to 10.1.2.3:443")));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        await AssertErrorMessageAsync(response, cancellationToken);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain("10.1.2.3", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_ApiTimesOut_ShowsErrorMessageWithoutFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Throwing(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 100 seconds elapsing.", new TimeoutException())));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        await AssertErrorMessageAsync(response, cancellationToken);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task AccountPage_ApiReturnsServerOrClientError_ShowsErrorMessageWithoutFailing(HttpStatusCode status)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(status, "{\"title\":\"boom\",\"detail\":\"SV-INTERNAL-DETAIL\"}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        await AssertErrorMessageAsync(response, cancellationToken);
        Assert.DoesNotContain("SV-INTERNAL-DETAIL", await response.Content.ReadAsStringAsync(cancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_ApiReturns401_RedirectsToSignInWithAccountAsReturnUrl()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.Unauthorized, string.Empty));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = Absolute(response);
        Assert.Equal("/account/sign-in", location.AbsolutePath);
        Assert.Equal("/account", WebTestSupport.QueryValue(location, "returnUrl"));
    }

    [Fact]
    public async Task AccountPage_ApiReturns403_RedirectsToAccessDenied()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.Forbidden, string.Empty));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/access-denied", Absolute(response).AbsolutePath);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"userId\":\"not-a-guid\",\"displayName\":\"x\",\"isAdmin\":false}")]
    [InlineData("{\"userId\":\"00000000-0000-0000-0000-000000000000\",\"displayName\":\"x\",\"isAdmin\":false}")]
    [InlineData("{\"userId\":12,\"displayName\":{},\"isAdmin\":\"maybe\"}")]
    [InlineData("{}")]
    public async Task AccountPage_ApiReturnsMalformedBody_ShowsErrorMessageAndNoProfile(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, body));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        await AssertErrorMessageAsync(response, cancellationToken);
    }

    [Fact]
    public async Task AccountPage_ApiReturnsNonJsonContentType_ShowsErrorMessageWithoutFailing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Respond(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>Proxy login page</html>", Encoding.UTF8, "text/html"),
        }));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        await AssertErrorMessageAsync(response, cancellationToken);
    }

    [Fact]
    public async Task AccountPage_TokenAcquisitionRequiresInteraction_RedirectsToSignIn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var acquisition = Substitute.For<ITokenAcquisition>();
        acquisition
            .GetAccessTokenForUserAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(_ => Task.FromException<string>(new MicrosoftIdentityWebChallengeUserException(
                new Microsoft.Identity.Client.MsalUiRequiredException("AADSTS65001", TokenAcquisitionMarker),
                ["scope"],
                null)));
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, "{}"), acquisition);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/sign-in", Absolute(response).AbsolutePath);
        Assert.False(factory.Logs.ContainsText(TokenAcquisitionMarker));
    }

    [Fact]
    public async Task AccountPage_TokenAcquisitionFails_ShowsGenericErrorPageWithoutDetails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var acquisition = Substitute.For<ITokenAcquisition>();
        acquisition
            .GetAccessTokenForUserAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(_ => Task.FromException<string>(new InvalidOperationException(TokenAcquisitionMarker)));
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, "{}"), acquisition);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.DoesNotContain(TokenAcquisitionMarker, html, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AccountPage_SignedIn_ResponseHasSecurityHeadersAndNoStore()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"Name\",\"isAdmin\":false}}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        SecurityHeaderAssertions.AssertWeb(response);
        Assert.Contains("no-store", response.HeaderValue("Cache-Control"), StringComparison.Ordinal);
        Assert.False(WebTestSupport.HasInlineScriptHandlerOrStyle(await response.Content.ReadAsStringAsync(cancellationToken)));
    }

    [Fact]
    public async Task AccountPage_SignedIn_HtmlContainsNoAccessTokenOrSegmentOfIt()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var issued = new List<string>();
        var acquisition = Substitute.For<ITokenAcquisition>();
        acquisition
            .GetAccessTokenForUserAsync(default!, default, default, default, default, default)
            .ReturnsForAnyArgs(call =>
            {
                var oid = Guid.Parse(call.ArgAt<ClaimsPrincipal>(4).FindFirst("oid")!.Value);
                var token = TestTokens.ForObjectId(oid);
                issued.Add(token);
                return Task.FromResult(token);
            });
        using var host = CreateHost(StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"Name\",\"isAdmin\":false}}"), acquisition);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);

        var response = await client.GetAsync("/account", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var token = Assert.Single(issued);
        Assert.DoesNotContain("eyJ", html, StringComparison.Ordinal);
        foreach (var segment in token.Split('.'))
        {
            Assert.DoesNotContain(segment, html, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(WebTestSupport.SetCookies(response), cookie => cookie.Contains(token[..30], StringComparison.Ordinal));
        Assert.DoesNotContain("Bearer ", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AccountPage_SignedIn_DoesNotLogTokensNamesOrEmails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stub = StubApi.Json(HttpStatusCode.OK, $"{{\"userId\":\"{Guid.NewGuid()}\",\"displayName\":\"Unique Person Name\",\"isAdmin\":false}}");
        using var host = CreateHost(stub);
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);
        factory.Logs.Clear();

        await client.GetAsync("/account", cancellationToken);

        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        Assert.False(factory.Logs.ContainsText(token.Split('.')[1]), "The token payload was logged.");
        Assert.False(factory.Logs.ContainsText("Unique Person Name"), "The display name was logged.");
        Assert.False(factory.Logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
        Assert.False(factory.Logs.ContainsText("Bearer eyJ"), "An authorization header value was logged.");
    }

    [Fact]
    public async Task AccountPage_ApiFailure_DoesNotLogTokensNamesOrEmails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var host = CreateHost(StubApi.Json(HttpStatusCode.InternalServerError, "{\"detail\":\"Unique Person Name\"}"));
        using var client = await WebSignIn.CreateClientAsync(host, TestUsers.User, cancellationToken);
        factory.Logs.Clear();

        await client.GetAsync("/account", cancellationToken);

        var token = TestTokens.ForObjectId(TestUsers.User.ObjectId);
        Assert.False(factory.Logs.ContainsText(token.Split('.')[1]), "The token payload was logged.");
        Assert.False(factory.Logs.ContainsText("Unique Person Name"), "API response content was logged.");
        Assert.False(factory.Logs.ContainsText(TestUsers.User.Email!), "The email address was logged.");
    }

    [Fact]
    public async Task AccountPage_Anonymous_NeverCallsTheApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var stub = StubApi.Json(HttpStatusCode.OK, "{}");
        using var host = CreateHost(stub);
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false });

        var response = await client.GetAsync("/account", cancellationToken);

        WebTestSupport.AssertTenantAuthorizeRedirect(response);
        Assert.Empty(stub.Requests);
    }

    private static Uri Absolute(HttpResponseMessage response) => new(new Uri("https://localhost"), response.Headers.Location!);

    private static async Task AssertErrorMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("id=\"account-error\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-section\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-display-name\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("id=\"account-user-id\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
    }

    private WebApplicationFactory<WebApp::Program> CreateHost(StubApi stub, ITokenAcquisition? acquisition = null) =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (acquisition is not null)
            {
                services.AddSingleton(acquisition);
            }

            services.AddHttpClient<ISharpVaultApiClient, SharpVaultApiClient>()
                .ConfigurePrimaryHttpMessageHandler(() => stub);
        }));

    internal sealed class StubApi : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _respond;
        private readonly List<RecordedRequest> _requests = [];

        private StubApi(Func<HttpResponseMessage> respond) => _respond = respond;

        public IReadOnlyList<RecordedRequest> Requests
        {
            get
            {
                lock (_requests)
                {
                    return [.. _requests];
                }
            }
        }

        public static StubApi Json(HttpStatusCode status, string body) =>
            new(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

        public static StubApi Throwing(Exception exception) => new(() => throw exception);

        public static StubApi Respond(Func<HttpResponseMessage> respond) => new(respond);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_requests)
            {
                _requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization));
            }

            return Task.FromResult(_respond());
        }
    }

    internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, AuthenticationHeaderValue? Authorization);
}
