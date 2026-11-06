// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed class ApiClientBaseTests : IDisposable
{
    private const string Token = "eyJ.super-secret-access-token.signature";
    private const string ProfileJson = """{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice Example","isAdmin":true}""";
    private const string ConfigurationMismatchLog = "The API rejected the local account token. Check that SharpVault.Api runs in local mode and shares the local signing key.";
    private const string Route = "/api/me";
    private const string FolderRoute = "/api/vault/folders";
    private const string FolderJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","parentId":null,"name":"Travel","createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","isDamaged":false}""";
    private const string ETag = "W/\"0x8DC1A2B3C4D5E6F\"";

    private readonly IApiAccessTokenProvider _tokens = Substitute.For<IApiAccessTokenProvider>();
    private readonly AuthenticationStateProvider _authentication = Substitute.For<AuthenticationStateProvider>();
    private readonly RecordingLogger<ProbeClient> _logger = new();
    private readonly ISessionValidator _session = Substitute.For<ISessionValidator>();
    private readonly StubHttpHandler _handler = new();
    private readonly ClaimsPrincipal _principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"));

    public ApiClientBaseTests()
    {
        _session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        _tokens.InteractiveSignInAvailable.Returns(true);
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer(Token));
        _authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(_principal));
        _handler.Respond(HttpStatusCode.OK, ProfileJson);
    }

    public void Dispose() => _handler.Dispose();

    private ProbeClient Client(string baseAddress = "https://api.example.test/") =>
        new(new HttpClient(_handler) { BaseAddress = new Uri(baseAddress) }, _tokens, _authentication, _session, _logger);

    [Fact]
    public async Task Get_UnauthenticatedPrincipal_ReturnsSignInRequiredWithoutCallingTheApi()
    {
        _authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity())));

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
        await _tokens.DidNotReceiveWithAnyArgs().GetAccessTokenAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_SessionNoLongerActive_ReturnsSignInRequiredWithoutCallingTheApi()
    {
        _session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
        await _tokens.DidNotReceiveWithAnyArgs().GetAccessTokenAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_TokenSignInRequired_ReturnsSignInRequiredWithoutCallingTheApi()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.SignInRequired);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Get_Success_SendsBearerTokenAndRelativeRoute()
    {
        var response = await Client("https://api.example.test/sub/").ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal("Alice Example", response.Value!.DisplayName);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(new Uri("https://api.example.test/sub/api/me"), request.Uri);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(Token, request.AuthorizationParameter);
        await _tokens.Received(1).GetAccessTokenAsync(_principal, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_NoTokenKind_SendsNoAuthorizationHeader()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.NoToken);

        await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(_handler.Requests).AuthorizationScheme);
    }

    [Fact]
    public async Task Get_BearerWithNullValue_SendsNoAuthorizationHeader()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(new ApiAccessToken(ApiAccessTokenKind.Bearer, null));

        await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(_handler.Requests).AuthorizationScheme);
    }

    [Fact]
    public async Task Get_401InteractiveProvider_ReturnsSignInRequired()
    {
        _handler.Respond(HttpStatusCode.Unauthorized, string.Empty);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.DoesNotContain(_logger.Records, record => record.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Get_401LocalProvider_ReturnsConfigurationMismatch()
    {
        _tokens.InteractiveSignInAvailable.Returns(false);
        _handler.Respond(HttpStatusCode.Unauthorized, string.Empty);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.ConfigurationMismatch, response.Status);
        Assert.Equal(ConfigurationMismatchLog, Assert.Single(_logger.Records, record => record.Level == LogLevel.Warning).Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Get_403_ReturnsAccessDenied(bool interactive)
    {
        _tokens.InteractiveSignInAvailable.Returns(interactive);
        _handler.Respond(HttpStatusCode.Forbidden, string.Empty);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.AccessDenied, response.Status);
        Assert.Null(response.Value);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Get_500_ReturnsFailedAndLogsStatusOnly(HttpStatusCode status)
    {
        _handler.Respond(status, "internal stack trace with secret-body-text");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        var warning = Assert.Single(_logger.Records, record => record.Level == LogLevel.Warning);
        Assert.Contains(((int)status).ToString(CultureInfo.InvariantCulture), warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-body-text", _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_HttpRequestException_ReturnsFailed()
    {
        _handler.Throw(new HttpRequestException("connection refused to secret-host"));

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.DoesNotContain("secret-host", _logger.AllText(), StringComparison.Ordinal);
        Assert.Contains(nameof(HttpRequestException), _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get_ClientTimeout_ReturnsFailed()
    {
        _handler.Throw(new TaskCanceledException("timeout"));

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task Get_CallerCancellation_Propagates()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client().ReadAsync(Route, source.Token));
    }

    [Fact]
    public async Task Get_NonJsonContentType_ReturnsFailed()
    {
        _handler.Respond(HttpStatusCode.OK, "<html>gateway</html>", "text/html");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"userId":"not-a-guid","displayName":"x","isAdmin":false}""")]
    public async Task Get_InvalidJson_ReturnsFailed(string body)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Null(response.Value);
    }

    [Fact]
    public async Task Get_ValueRejectedByIsValid_ReturnsFailed()
    {
        var response = await Client().ReadRejectingAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task Get_TokenProviderThrows_ReturnsFailedAndLogsNoTokenOrExceptionText()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("leaked eyJ.token.value"));

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Empty(_handler.Requests);
        Assert.DoesNotContain("leaked", _logger.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", _logger.AllText(), StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), _logger.AllText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Get_Logs_NeverContainTokensNamesOrEmails(HttpStatusCode status)
    {
        _tokens.InteractiveSignInAvailable.Returns(false);
        _handler.Respond(status, status == HttpStatusCode.OK ? ProfileJson : "Alice Example alice@example.test");

        await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        var text = _logger.AllText();
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_SendsJsonBodyAndBearerToken()
    {
        _handler.Respond(HttpStatusCode.Created, FolderJson);

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "Travel" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal("""{"name":"Travel","parentId":null}""", request.Body);
        Assert.Equal("Bearer", request.AuthorizationScheme);
        Assert.Equal(Token, request.AuthorizationParameter);
        Assert.Null(request.IfMatch);
    }

    [Fact]
    public async Task Post_IfMatch_IsSentUnchanged()
    {
        _handler.Respond(HttpStatusCode.OK, FolderJson);

        await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "Travel" }, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ETag, Assert.Single(_handler.Requests).IfMatch);
    }

    [Fact]
    public async Task Post_201_ReadsTheBody()
    {
        _handler.Respond(HttpStatusCode.Created, FolderJson);

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "Travel" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal("Travel", response.Value!.Name);
    }

    [Fact]
    public async Task Post_NoBody_SendsNoContentButTheTokenAndIfMatch()
    {
        _handler.Respond(HttpStatusCode.OK, FolderJson);

        var response = await Client().PostWithoutBodyAsync(FolderRoute, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        var request = Assert.Single(_handler.Requests);
        Assert.Null(request.Body);
        Assert.Equal(ETag, request.IfMatch);
        Assert.Equal(Token, request.AuthorizationParameter);
    }

    [Fact]
    public async Task Put_SendsJsonBodyAndIfMatch()
    {
        _handler.Respond(HttpStatusCode.OK, FolderJson);

        var response = await Client().ReplaceAsync(FolderRoute, new RenameFolderRequestDto { Name = "Trips" }, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("""{"name":"Trips"}""", request.Body);
        Assert.Equal(ETag, request.IfMatch);
    }

    [Fact]
    public async Task Delete_204_ReturnsSuccessTrue()
    {
        _handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await Client().RemoveAsync(FolderRoute, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.True(response.Value);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Delete, request.Method);
        Assert.Equal(ETag, request.IfMatch);
    }

    [Fact]
    public async Task Get_204ForNonBooleanResult_ReturnsFailed()
    {
        _handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task Send_400WithDetail_ReturnsInvalidWithDetail()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"title":"Bad Request","detail":"The folder name is too long."}""", "application/problem+json");

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "x" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Invalid, response.Status);
        Assert.Equal("The folder name is too long.", response.Detail);
        Assert.Null(response.Value);
    }

    [Fact]
    public async Task Send_400WithValidationErrors_ReturnsInvalidWithFirstMessage()
    {
        _handler.Respond(HttpStatusCode.BadRequest, """{"errors":{"Name":["","Name is required."],"Other":["Second."]}}""", "application/problem+json");

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "x" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Invalid, response.Status);
        Assert.Equal("Name is required.", response.Detail);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("[1,2]")]
    [InlineData("""{"errors":{"Name":null}}""")]
    [InlineData("""{"detail":"   "}""")]
    public async Task Send_400WithUnreadableBody_ReturnsInvalidWithoutDetail(string body)
    {
        _handler.Respond(HttpStatusCode.BadRequest, body);

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "x" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Invalid, response.Status);
        Assert.Null(response.Detail);
    }

    [Fact]
    public async Task Send_404_ReturnsNotFoundWithoutWarningLog()
    {
        _handler.Respond(HttpStatusCode.NotFound, "no such secret");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.NotFound, response.Status);
        Assert.DoesNotContain(_logger.Records, record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Send_409_ReturnsConflict()
    {
        _handler.Respond(HttpStatusCode.Conflict, "stale");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Conflict, response.Status);
        Assert.DoesNotContain(_logger.Records, record => record.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task Send_429_ReturnsRateLimited()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, "slow down");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.RateLimited, response.Status);
        Assert.DoesNotContain(_logger.Records, record => record.Level >= LogLevel.Warning);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task Send_NonBadRequestStatusWithProblemDetail_NeverCarriesTheDetail(HttpStatusCode status)
    {
        _handler.Respond(status, """{"detail":"Server stack trace for secret 4711","errors":{"Name":["Internal message"]}}""", "application/problem+json");

        var response = await Client().ReadAsync(Route, TestContext.Current.CancellationToken);

        Assert.NotEqual(ApiCallStatus.Invalid, response.Status);
        Assert.Null(response.Detail);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Post_ServerError_LogsNoBodyOrValue()
    {
        _handler.Respond(HttpStatusCode.InternalServerError, "echo of hunter2-secret-value");

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "hunter2-secret-value" }, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        var text = _logger.AllText();
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, text, StringComparison.Ordinal);
        Assert.DoesNotContain("0x8DC1A2B3C4D5E6F", text, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("/api/vault/secrets/{secretId:guid}")]
    [InlineData("/api/vault/secrets/{secretId:guid}/history/3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b/reveal")]
    [InlineData("/api/vault/folders?x={")]
    public async Task ApiClientBase_RouteWithUnboundPlaceholder_ThrowsBeforeAnyCall(string route)
    {
        var client = Client();
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentException>(() => client.ReadAsync(route, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.CreateAsync(route, new CreateFolderRequestDto { Name = "x" }, null, cancellationToken));
        await Assert.ThrowsAsync<ArgumentException>(() => client.RemoveAsync(route, null, cancellationToken));

        Assert.Empty(_handler.Requests);
        await _tokens.DidNotReceiveWithAnyArgs().GetAccessTokenAsync(default!, cancellationToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Send_TwoCalls_RequestAFreshTokenForEachCall()
    {
        var client = Client();
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(ApiAccessToken.Bearer("first-token"), ApiAccessToken.Bearer("second-token"));

        await client.ReadAsync(Route, TestContext.Current.CancellationToken);
        await client.ReadAsync(Route, TestContext.Current.CancellationToken);

        await _tokens.Received(2).GetAccessTokenAsync(_principal, Arg.Any<CancellationToken>());
        Assert.Equal(["first-token", "second-token"], _handler.Requests.Select(request => request.AuthorizationParameter));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Send_SessionNoLongerActive_SendsNoBodyAndNoToken()
    {
        _session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(false);

        var response = await Client().CreateAsync(FolderRoute, new CreateFolderRequestDto { Name = "x" }, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.SignInRequired, response.Status);
        Assert.Empty(_handler.Requests);
        await _tokens.DidNotReceiveWithAnyArgs().GetAccessTokenAsync(default!, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Get_BlankRoute_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Client().ReadAsync(" ", TestContext.Current.CancellationToken));
    }

    internal sealed class ProbeClient(
        HttpClient httpClient,
        IApiAccessTokenProvider tokenProvider,
        AuthenticationStateProvider authenticationStateProvider,
        ISessionValidator sessionValidator,
        ILogger<ProbeClient> logger) : ApiClientBase(httpClient, tokenProvider, authenticationStateProvider, sessionValidator, logger)
    {
        private bool _rejectEverything;

        public Task<ApiResponse<CurrentUserDto>> ReadAsync(string route, CancellationToken cancellationToken) =>
            GetAsync<CurrentUserDto>(route, cancellationToken);

        public Task<ApiResponse<FolderDto>> CreateAsync(string route, CreateFolderRequestDto body, string? ifMatch, CancellationToken cancellationToken) =>
            PostAsync<CreateFolderRequestDto, FolderDto>(route, body, ifMatch, cancellationToken);

        public Task<ApiResponse<FolderDto>> PostWithoutBodyAsync(string route, string? ifMatch, CancellationToken cancellationToken) =>
            PostAsync<FolderDto>(route, ifMatch, cancellationToken);

        public Task<ApiResponse<FolderDto>> ReplaceAsync(string route, RenameFolderRequestDto body, string? ifMatch, CancellationToken cancellationToken) =>
            PutAsync<RenameFolderRequestDto, FolderDto>(route, body, ifMatch, cancellationToken);

        public Task<ApiResponse<bool>> RemoveAsync(string route, string? ifMatch, CancellationToken cancellationToken) =>
            DeleteAsync(route, ifMatch, cancellationToken);

        public Task<ApiResponse<CurrentUserDto>> ReadRejectingAsync(string route, CancellationToken cancellationToken)
        {
            _rejectEverything = true;
            return GetAsync<CurrentUserDto>(route, cancellationToken);
        }

        protected override bool IsValid<T>(T value) => !_rejectEverything;
    }
}
