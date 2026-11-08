// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Authentication.WebGateway;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.OneTimeLinks;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class OneTimeLinkGatewayClientTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string UserAgent = "SharpVault-Test-Browser/1.0";
    private const string ClientIp = "203.0.113.50";
    private const string Body = "{\"payload\":\"AAAA\"}";
    private const string NotFoundBody = "{\"status\":\"unavailable\"}";
    private const string SecretDetail = "secret-detail-marker";

    private static readonly DateTimeOffset Start = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    private static readonly Guid LinkId = new("6f1c2b3a-0000-4000-8000-00000000abcd");

    [Fact]
    public async Task Open_NoKeyConfigured_MakesNoCallAndReturnsUnavailable()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, Body));
        var client = Create(handler, key: null, new FakeTimeProvider(Start), out _);

        var result = await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        Assert.Equal(OneTimeLinkOpenOutcome.Unavailable, result.Outcome);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Open_SignsThePathTimeAddressAndAgent()
    {
        var time = new FakeTimeProvider(Start);
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.NotFound, NotFoundBody));
        var client = Create(handler, Key, time, out _);

        await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        var path = $"/api/one-time-links/{LinkId:D}/open";
        Assert.Equal(HttpMethod.Post.Method, request.Method);
        Assert.Equal(path, request.Path);
        Assert.Equal(Start.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture), request.Headers[WebGatewayHeaders.Time]);
        Assert.Equal(ClientIp, request.Headers[WebGatewayHeaders.ClientIp]);
        var agent = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(UserAgent));
        Assert.Equal(agent, request.Headers[WebGatewayHeaders.ClientAgent]);
        Assert.True(WebGatewaySignature.Verify(
            Key,
            WebGatewayPurposes.LinkOpen,
            WebGatewayScope.ForRoute(HttpMethod.Post.Method, path),
            Start.ToUnixTimeSeconds(),
            ClientIp,
            agent,
            request.Headers[WebGatewayHeaders.Signature],
            Start));
    }

    [Fact]
    public async Task Open_ApiReturns404_ReturnsUnavailable()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.NotFound, NotFoundBody));
        var client = Create(handler, Key, new FakeTimeProvider(Start), out _);

        var result = await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        Assert.Equal(OneTimeLinkOpenOutcome.Unavailable, result.Outcome);
    }

    [Fact]
    public async Task Open_ApiReturns500_ReturnsUnavailable()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.InternalServerError, string.Empty));
        var client = Create(handler, Key, new FakeTimeProvider(Start), out var logs);

        var result = await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        Assert.Equal(OneTimeLinkOpenOutcome.Unavailable, result.Outcome);
        Assert.Contains("500", logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_ApiThrows_ReturnsUnavailableAndLogsOnlyTheType()
    {
        var handler = new ScriptedHandler(_ => throw new InvalidOperationException(SecretDetail));
        var client = Create(handler, Key, new FakeTimeProvider(Start), out var logs);

        var result = await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        Assert.Equal(OneTimeLinkOpenOutcome.Unavailable, result.Outcome);
        Assert.Contains(nameof(InvalidOperationException), logs.AllText, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretDetail, logs.AllText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Open_ApiReturns429_ReturnsRateLimitedWithRetryAfter()
    {
        var handler = new ScriptedHandler(_ =>
        {
            var response = Respond(HttpStatusCode.TooManyRequests, NotFoundBody);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return response;
        });
        var client = Create(handler, Key, new FakeTimeProvider(Start), out _);

        var result = await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        Assert.Equal(OneTimeLinkOpenOutcome.RateLimited, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(30), result.RetryAfter);
    }

    [Fact]
    public async Task Open_CallerCancelled_Rethrows()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.OK, Body));
        var client = Create(handler, Key, new FakeTimeProvider(Start), out _);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.OpenAsync(LinkId, ClientIp, UserAgent, cancellation.Token));
    }

    [Fact]
    public async Task Open_AnyOutcome_NeverLogsTheLinkIdOrASignature()
    {
        var handler = new ScriptedHandler(_ => Respond(HttpStatusCode.InternalServerError, string.Empty));
        var client = Create(handler, Key, new FakeTimeProvider(Start), out var logs);

        await client.OpenAsync(LinkId, ClientIp, UserAgent, TestContext.Current.CancellationToken);

        var request = Assert.Single(handler.Requests);
        Assert.DoesNotContain(LinkId.ToString("D"), logs.AllText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(request.Headers[WebGatewayHeaders.Signature], logs.AllText, StringComparison.Ordinal);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static OneTimeLinkGatewayClient Create(ScriptedHandler handler, string? key, TimeProvider time, out CapturingLoggerProvider logs)
    {
        var provider = new CapturingLoggerProvider();
        logs = provider;
        using var factory = LoggerFactory.Create(builder => builder.SetMinimumLevel(LogLevel.Debug).AddProvider(provider));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.test/") };
        return new OneTimeLinkGatewayClient(
            http,
            Options.Create(new WebGatewayOptions { Key = key }),
            time,
            factory.CreateLogger<OneTimeLinkGatewayClient>());
    }

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<CapturedRequest> _requests = [];

        public IReadOnlyList<CapturedRequest> Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _requests.Add(new CapturedRequest(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(respond(request));
        }
    }

    private sealed record CapturedRequest(string Method, string Path, IReadOnlyDictionary<string, string> Headers);
}
