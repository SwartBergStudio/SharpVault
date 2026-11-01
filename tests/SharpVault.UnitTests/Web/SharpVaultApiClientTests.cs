// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.UnitTests.Web;

/// <summary>
/// W11: the API client calls only the configured base address, never logs the token, and maps statuses safely.
/// </summary>
[Trait("Category", "Security")]
public sealed class SharpVaultApiClientTests : IDisposable
{
    private const string Token = "eyJ.super-secret-access-token.signature";
    private const string ProfileJson = """{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice Example","isAdmin":true}""";

    private static readonly string[] Scopes = ["api://a1b2c3d4/access_as_user"];

    private readonly ITokenAcquisition _tokens = Substitute.For<ITokenAcquisition>();
    private readonly AuthenticationStateProvider _authentication = Substitute.For<AuthenticationStateProvider>();
    private readonly RecordingLogger<SharpVaultApiClient> _logger = new();
    private readonly RecordingHandler _handler = new();
    private readonly ClaimsPrincipal _principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"));

    public SharpVaultApiClientTests()
    {
        _tokens.GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .Returns(Token);
        _authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(_principal));
        _handler.Respond(HttpStatusCode.OK, ProfileJson);
    }

    public void Dispose() => _handler.Dispose();

    private SharpVaultApiClient Client(string baseAddress = "https://api.example.test/")
    {
        var options = Options.Create(new SharpVaultApiOptions { BaseAddress = new Uri(baseAddress), Scopes = Scopes });
        var http = new HttpClient(_handler) { BaseAddress = options.Value.BaseAddress };
        return new SharpVaultApiClient(http, _tokens, _authentication, options, _logger);
    }

    [Fact]
    public async Task GetCurrentUserAsync_SignedIn_SendsBearerTokenToConfiguredBaseAddress()
    {
        await Client("https://api.example.test/sub/").GetCurrentUserAsync(TestContext.Current.CancellationToken);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(new Uri("https://api.example.test/sub/api/me"), request.Uri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(Token, request.AuthorizationParameter);
    }

    [Fact]
    public async Task GetCurrentUserAsync_SignedIn_RequestsConfiguredScopesForThatPrincipal()
    {
        await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        await _tokens.Received(1).GetAccessTokenForUserAsync(
            Arg.Is<IEnumerable<string>>(scopes => scopes.SequenceEqual(Scopes)),
            null,
            null,
            null,
            _principal,
            null);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Anonymous_ReturnsSignInRequiredWithoutTokenOrHttpCall()
    {
        _authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
        Assert.Empty(_tokens.ReceivedCalls());
    }

    [Fact]
    public async Task GetCurrentUserAsync_ChallengeException_ReturnsSignInRequiredWithoutHttpCall()
    {
        _tokens.GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .ThrowsAsync(new MicrosoftIdentityWebChallengeUserException(new MsalUiRequiredException("code", "message"), ["scope"]));

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Api200_ReturnsProfile()
    {
        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.NotNull(response.Value);
        Assert.Equal(Guid.Parse("3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b"), response.Value.UserId);
        Assert.Equal("Alice Example", response.Value.DisplayName);
        Assert.True(response.Value.IsAdmin);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Api200NullBody_ReturnsFailed()
    {
        _handler.Respond(HttpStatusCode.OK, "null");

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Null(response.Value);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("<html>gateway error</html>")]
    [InlineData("")]
    [InlineData("""{"userId":"not-a-guid","displayName":"x","isAdmin":false}""")]
    public async Task GetCurrentUserAsync_Api200MalformedBody_ReturnsFailed(string body)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ApiUnreachable_ReturnsFailed()
    {
        _handler.Throw(new HttpRequestException("connection refused"));

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Api401_ReturnsSignInRequired()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, string.Empty);

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Api403_ReturnsAccessDenied()
    {
        _handler.Respond(HttpStatusCode.Forbidden, string.Empty);

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
        Assert.Null(response.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetCurrentUserAsync_OtherStatus_ReturnsFailedAndLogsStatusOnly(HttpStatusCode status)
    {
        _handler.Respond(status, "internal stack trace with secret-body-text");

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        var warning = Assert.Single(_logger.Records, record => record.Level == LogLevel.Warning);
        Assert.Contains(((int)status).ToString(System.Globalization.CultureInfo.InvariantCulture), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-body-text", _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentUserAsync_Api500_ReturnsFailedAndLogsStatusOnly()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "boom");

        var response = await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Contains("500", Assert.Single(_logger.Records).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetCurrentUserAsync_AnyOutcome_TokenIsNotLogged(HttpStatusCode status)
    {
        _handler.Respond(status, status == HttpStatusCode.OK ? ProfileJson : string.Empty);

        await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain(Token, _logger.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ChallengeException_TokenAndScopesAreNotLogged()
    {
        _tokens.GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .ThrowsAsync(new MicrosoftIdentityWebChallengeUserException(new MsalUiRequiredException("code", "message with secret"), ["scope"]));

        await Client().GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.DoesNotContain("secret", _logger.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("access_as_user", _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetCurrentUserAsync_CancelledToken_PropagatesCancellation()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().GetCurrentUserAsync(source.Token));
    }

    [Fact]
    public void ApiCallStatus_Default_IsFailed()
    {
        Assert.Equal(ApiCallStatus.Failed, default(ApiCallStatus));
        Assert.Equal(ApiCallStatus.Failed, default(ApiResponse<string>)?.Status ?? ApiCallStatus.Failed);
    }

    [Fact]
    public void ApiResponse_Factories_SetStatusAndValue()
    {
        Assert.Equal(ApiCallStatus.Success, ApiResponse<string>.Success("x").Status);
        Assert.Equal("x", ApiResponse<string>.Success("x").Value);
        Assert.Equal(ApiCallStatus.SignInRequired, ApiResponse<string>.SignInRequired().Status);
        Assert.Equal(ApiCallStatus.AccessDenied, ApiResponse<string>.AccessDenied().Status);
        Assert.Equal(ApiCallStatus.Failed, ApiResponse<string>.Failed().Status);
        Assert.Null(ApiResponse<string>.Failed().Value);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<RecordedRequest> _requests = [];
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = string.Empty;
        private Exception? _failure;

        public IReadOnlyList<RecordedRequest> Requests => _requests;

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
            _failure = null;
        }

        public void Throw(Exception failure) => _failure = failure;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Add(new RecordedRequest(
                request.RequestUri!,
                request.Method,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));

            if (_failure is not null)
            {
                throw _failure;
            }

            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed record RecordedRequest(Uri Uri, HttpMethod Method, string? AuthorizationScheme, string? AuthorizationParameter);
}
