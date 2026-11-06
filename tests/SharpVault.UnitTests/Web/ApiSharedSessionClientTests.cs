// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using NSubstitute;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class ApiSharedSessionClientTests
{
    private const string SessionIdValue = "0123456789abcdef0123456789abcdef";
    private const string BearerValue = "bearer-secret-value";
    private const string BaseAddress = "https://api.test/";
    private const string CheckPath = "api/me/session/check";
    private const string RevokePath = "api/me/session/revoke";
    private const int ExpirySeconds = 1893456000;
    private const int ServerErrorStatus = 500;
    private const int ThrottledStatus = 429;
    private const int NotFoundStatus = 404;
    private const int UnauthorizedStatus = 401;

    private readonly IApiAccessTokenProvider _tokens = Substitute.For<IApiAccessTokenProvider>();
    private readonly CapturingLogger _logger = new();

    public ApiSharedSessionClientTests()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(new ApiAccessToken(ApiAccessTokenKind.Bearer, BearerValue));
    }

    public static TheoryData<int, string, SharedSessionState> StatusMapping() =>
        new()
        {
            { (int)HttpStatusCode.OK, "{\"isActive\":true}", SharedSessionState.Active },
            { (int)HttpStatusCode.OK, "{\"isActive\":false}", SharedSessionState.Ended },
            { (int)HttpStatusCode.Forbidden, string.Empty, SharedSessionState.Forbidden },
            { UnauthorizedStatus, string.Empty, SharedSessionState.Unknown },
            { NotFoundStatus, string.Empty, SharedSessionState.Unknown },
            { ThrottledStatus, string.Empty, SharedSessionState.Unknown },
            { ServerErrorStatus, string.Empty, SharedSessionState.Unknown },
        };

    [Theory]
    [MemberData(nameof(StatusMapping))]
    public async Task SharedSessionClient_Check_MapsEveryStatus(int status, string body, SharedSessionState expected)
    {
        var client = CreateClient(_ => Response((HttpStatusCode)status, body));

        var state = await client.CheckAsync(Principal(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, state);
    }

    [Fact]
    public async Task SharedSessionClient_Check_TokenSignInRequired_IsUnknownAndMakesNoCall()
    {
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(new ApiAccessToken(ApiAccessTokenKind.SignInRequired, null));
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, "{\"isActive\":true}"));
        var client = new ApiSharedSessionClient(new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) }, _tokens, _logger);

        var state = await client.CheckAsync(Principal(), TestContext.Current.CancellationToken);

        Assert.Equal(SharedSessionState.Unknown, state);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SharedSessionClient_Check_MalformedClaims_IsUnknownAndMakesNoCall()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, "{\"isActive\":true}"));
        var client = new ApiSharedSessionClient(new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) }, _tokens, _logger);

        var state = await client.CheckAsync(new ClaimsPrincipal(new ClaimsIdentity()), TestContext.Current.CancellationToken);

        Assert.Equal(SharedSessionState.Unknown, state);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SharedSessionClient_Check_Exception_IsUnknown()
    {
        var client = CreateClient(_ => throw new HttpRequestException("The network is down."));

        var state = await client.CheckAsync(Principal(), TestContext.Current.CancellationToken);

        Assert.Equal(SharedSessionState.Unknown, state);
    }

    [Fact]
    public async Task SharedSessionClient_Check_TokenNeverReturns_ObservesTheCallerCancellation()
    {
        using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        _tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns<Task<ApiAccessToken>>(_ => new TaskCompletionSource<ApiAccessToken>().Task);
        var client = CreateClient(_ => Response(HttpStatusCode.OK, "{\"isActive\":true}"));
        var check = client.CheckAsync(Principal(), cancelled.Token);

        var finished = await Task.WhenAny(check, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(check, finished);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
    }

    [Fact]
    public async Task SharedSessionClient_Check_Cancelled_Propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var client = CreateClient(_ => throw new OperationCanceledException(cancelled.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.CheckAsync(Principal(), cancelled.Token));
    }

    [Fact]
    public async Task SharedSessionClient_Revoke_NeverThrows()
    {
        var failing = CreateClient(_ => throw new HttpRequestException("The network is down."));
        var refused = CreateClient(_ => Response(HttpStatusCode.InternalServerError, string.Empty));

        await failing.RevokeAsync(Principal(), TestContext.Current.CancellationToken);
        await refused.RevokeAsync(Principal(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SharedSessionClient_Revoke_PostsTheSessionToTheRevokeRoute()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, "{\"isActive\":false}"));
        var client = new ApiSharedSessionClient(new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) }, _tokens, _logger);

        await client.RevokeAsync(Principal(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, handler.LastMethod);
        Assert.EndsWith(RevokePath, handler.LastUri?.ToString() ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedSessionClient_Failures_LogTypeOrStatusOnly()
    {
        var failing = CreateClient(_ => throw new HttpRequestException(SessionIdValue + BearerValue));
        var refused = CreateClient(_ => Response((HttpStatusCode)ServerErrorStatus, SessionIdValue));

        await failing.CheckAsync(Principal(), TestContext.Current.CancellationToken);
        await refused.RevokeAsync(Principal(), TestContext.Current.CancellationToken);

        Assert.DoesNotContain(SessionIdValue, _logger.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(BearerValue, _logger.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SharedSessionClient_Requests_PutNoSessionIdOrTokenInTheUrl()
    {
        var handler = new StubHandler(_ => Response(HttpStatusCode.OK, "{\"isActive\":true}"));
        var client = new ApiSharedSessionClient(new HttpClient(handler) { BaseAddress = new Uri(BaseAddress) }, _tokens, _logger);

        await client.CheckAsync(Principal(), TestContext.Current.CancellationToken);

        var url = handler.LastUri?.ToString() ?? string.Empty;
        Assert.DoesNotContain(SessionIdValue, url, StringComparison.Ordinal);
        Assert.DoesNotContain(BearerValue, url, StringComparison.Ordinal);
        Assert.EndsWith(CheckPath, url, StringComparison.Ordinal);
    }

    private ApiSharedSessionClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        new(new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri(BaseAddress) }, _tokens, _logger);

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity(
    [
        new Claim(SessionClaims.SessionId, SessionIdValue),
        new Claim(SessionClaims.ExpiresAt, ExpirySeconds.ToString(CultureInfo.InvariantCulture)),
    ], "test"));

    private static HttpResponseMessage Response(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastMethod = request.Method;
            LastUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<ApiSharedSessionClient>
    {
        private readonly List<string> _messages = [];

        public string Text => string.Join('\n', _messages);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(
            Microsoft.Extensions.Logging.LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            _messages.Add(formatter(state, exception));
    }
}
