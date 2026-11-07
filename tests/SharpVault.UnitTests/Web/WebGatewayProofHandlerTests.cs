// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.WebGateway;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Api;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class WebGatewayProofHandlerTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string BrowserAddress = "203.0.113.5";
    private const string ForgedAddress = "6.6.6.6";
    private const string JunkValue = "junk";
    private const string RequestUri = "https://api.test/api/me";
    private const long UnixSeconds = 1_800_000_000;
    private const int ResendDelaySeconds = 30;

    private static readonly Guid UserId = Guid.Parse("6f1c2b3a-0000-4000-8000-000000000001");
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(UnixSeconds);

    [Fact]
    public async Task Handler_PrincipalWithAddress_SendsAVerifiableProof()
    {
        var time = new FakeTimeProvider(Start);

        var sent = await SendAsync(Key, time, PrincipalWith(BrowserAddress), new CollectingLogger<WebGatewayProofHandler>());

        Assert.True(IsVerifiable(sent, Key, BrowserAddress, time.GetUtcNow()));
    }

    [Fact]
    public async Task Handler_PrincipalWithNoAddress_SendsAVerifiableEmptyAddressAndLogsAWarning()
    {
        var time = new FakeTimeProvider(Start);
        var logger = new CollectingLogger<WebGatewayProofHandler>();

        var sent = await SendAsync(Key, time, PrincipalWith(address: null), logger);

        Assert.Equal(string.Empty, sent.Headers[WebGatewayHeaders.ClientIp]);
        Assert.True(IsVerifiable(sent, Key, string.Empty, time.GetUtcNow()));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handler_NoKey_SendsNoHeaders()
    {
        var sent = await SendAsync(key: null, new FakeTimeProvider(Start), PrincipalWith(BrowserAddress), new CollectingLogger<WebGatewayProofHandler>());

        Assert.Empty(sent.Headers);
    }

    [Fact]
    public async Task Handler_NoPrincipalOption_SendsNoHeadersAndLogsAWarning()
    {
        var logger = new CollectingLogger<WebGatewayProofHandler>();

        var sent = await SendAsync(Key, new FakeTimeProvider(Start), principal: null, logger);

        Assert.Empty(sent.Headers);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Handler_InboundProofHeadersOnTheRequest_AreReplaced()
    {
        var time = new FakeTimeProvider(Start);
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Time, JunkValue);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.ClientIp, ForgedAddress);
        request.Headers.TryAddWithoutValidation(WebGatewayHeaders.Signature, JunkValue);

        var sent = await SendRequestAsync(request, Key, time, PrincipalWith(BrowserAddress), new CollectingLogger<WebGatewayProofHandler>());

        Assert.Equal(BrowserAddress, sent.Headers[WebGatewayHeaders.ClientIp]);
        Assert.True(IsVerifiable(sent, Key, BrowserAddress, time.GetUtcNow()));
    }

    [Fact]
    public async Task Handler_SecondSendOfTheSameRequest_SignsAgainWithTheNewTime()
    {
        var time = new FakeTimeProvider(Start);
        var inner = new CapturingHandler();
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        request.Options.Set(ApiRequestOptions.Principal, PrincipalWith(BrowserAddress));
        using var invoker = new HttpMessageInvoker(HandlerFor(Key, time, inner, new CollectingLogger<WebGatewayProofHandler>()));

        using var firstResponse = await invoker.SendAsync(request, CancellationToken.None);
        var first = inner.Sent!;
        time.Advance(TimeSpan.FromSeconds(ResendDelaySeconds));
        using var secondResponse = await invoker.SendAsync(request, CancellationToken.None);
        var second = inner.Sent!;

        Assert.NotEqual(first.Headers[WebGatewayHeaders.Time], second.Headers[WebGatewayHeaders.Time]);
        Assert.Equal((UnixSeconds + ResendDelaySeconds).ToString(CultureInfo.InvariantCulture), second.Headers[WebGatewayHeaders.Time]);
        Assert.True(IsVerifiable(second, Key, BrowserAddress, time.GetUtcNow()));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Handler_AnyOutcome_NeverLogsTheKeySignatureOrAddress()
    {
        var logger = new CollectingLogger<WebGatewayProofHandler>();
        var time = new FakeTimeProvider(Start);

        var withAddress = await SendAsync(Key, time, PrincipalWith(BrowserAddress), logger);
        var withoutAddress = await SendAsync(Key, time, PrincipalWith(address: null), logger);
        await SendAsync(key: null, time, PrincipalWith(BrowserAddress), logger);
        await SendAsync(Key, time, principal: null, logger);

        var logged = string.Join('\n', logger.Entries.Select(entry => entry.Message));
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(Key, logged);
        Assert.DoesNotContain(BrowserAddress, logged);
        Assert.DoesNotContain(withAddress.Headers[WebGatewayHeaders.Signature], logged);
        Assert.DoesNotContain(withoutAddress.Headers[WebGatewayHeaders.Signature], logged);
    }

    private static Task<SentRequest> SendAsync(
        string? key,
        FakeTimeProvider time,
        ClaimsPrincipal? principal,
        CollectingLogger<WebGatewayProofHandler> logger)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RequestUri);
        return SendRequestAsync(request, key, time, principal, logger);
    }

    private static async Task<SentRequest> SendRequestAsync(
        HttpRequestMessage request,
        string? key,
        FakeTimeProvider time,
        ClaimsPrincipal? principal,
        CollectingLogger<WebGatewayProofHandler> logger)
    {
        if (principal is not null)
        {
            request.Options.Set(ApiRequestOptions.Principal, principal);
        }

        var inner = new CapturingHandler();
        using var invoker = new HttpMessageInvoker(HandlerFor(key, time, inner, logger));
        using var response = await invoker.SendAsync(request, CancellationToken.None);
        return inner.Sent!;
    }

    private static WebGatewayProofHandler HandlerFor(
        string? key,
        TimeProvider time,
        CapturingHandler inner,
        CollectingLogger<WebGatewayProofHandler> logger) =>
        new(Options.Create(new WebGatewayOptions { Key = key }), time, logger) { InnerHandler = inner };

    private static ClaimsPrincipal PrincipalWith(string? address)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(EntraClaimTypes.ObjectId, UserId.ToString("D"))],
            "Test"));
        if (address is not null)
        {
            ClientAddressClaim.AddTo(principal, IPAddress.Parse(address));
        }

        return principal;
    }

    private static bool IsVerifiable(SentRequest sent, string key, string address, DateTimeOffset now)
    {
        if (!sent.Headers.TryGetValue(WebGatewayHeaders.Time, out var time)
            || !sent.Headers.TryGetValue(WebGatewayHeaders.ClientIp, out var ip)
            || !sent.Headers.TryGetValue(WebGatewayHeaders.Signature, out var signature))
        {
            return false;
        }

        return ip == address
            && long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds)
            && WebGatewaySignature.Verify(
                key,
                WebGatewayPurposes.ClientAddress,
                WebGatewayScope.For(UserId),
                unixSeconds,
                ip,
                string.Empty,
                signature,
                now);
    }

    private sealed record SentRequest(IReadOnlyDictionary<string, string> Headers);

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public SentRequest? Sent { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var headers = request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(',', header.Value),
                StringComparer.OrdinalIgnoreCase);
            Sent = new SentRequest(headers);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
