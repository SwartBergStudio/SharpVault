// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using AutoFixture;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Tracing;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class LoggingTests(ApiTestFactory factory) : BaseIntegrationTest(factory)
{
    private const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";

    [Fact]
    public async Task UnknownRoute_BearerTokenAndCookieSent_NeitherIsLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = ForgedTokens.SignedWithUnknownKey();
        var cookie = "session-" + Fixture.Create<Guid>().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("Cookie", $"session={cookie}");

        var response = await Client.SendAsync(request, cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEmpty(Logs.Entries);
        AssertLogsDoNotContain(token, cookie);
    }

    [Fact]
    public async Task UnknownRoute_TokenSegmentsSent_NoSegmentIsLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = ForgedTokens.SignedWithUnknownKey();
        var segments = token.Split('.');
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/secrets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await Client.SendAsync(request, cancellationToken);

        AssertLogsDoNotContain(segments);
    }

    [Fact]
    public async Task UnknownRoute_AlgNoneTokenSent_TokenIsNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = ForgedTokens.AlgNone();
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/secrets");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await Client.SendAsync(request, cancellationToken);

        AssertLogsDoNotContain(token, token.Split('.')[1]);
    }

    [Fact]
    public async Task UnknownRoute_ArbitraryRequestHeadersSent_ValuesAreNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var apiKey = "key-" + Fixture.Create<Guid>().ToString("N");
        var custom = "custom-" + Fixture.Create<Guid>().ToString("N");
        var request = new HttpRequestMessage(HttpMethod.Get, "/does-not-exist");
        request.Headers.Add("X-Api-Key", apiKey);
        request.Headers.Add("X-Custom-Secret", custom);

        await Client.SendAsync(request, cancellationToken);

        AssertLogsDoNotContain(apiKey, custom);
    }

    [Fact]
    public async Task UnknownRoute_RequestBodySent_BodyIsNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var secret = "body-secret-" + Fixture.Create<Guid>().ToString("N");
        using var content = new StringContent($"{{\"password\":\"{secret}\"}}", System.Text.Encoding.UTF8, "application/json");

        await Client.PostAsync("/api/v1/secrets", content, cancellationToken);

        AssertLogsDoNotContain(secret);
    }

    [Fact]
    public async Task UnknownRoute_SecretInQueryString_IsNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var secret = "query-secret-" + Fixture.Create<Guid>().ToString("N");

        await Client.GetAsync($"/does-not-exist?token={secret}", cancellationToken);

        AssertLogsDoNotContain(secret);
    }

    [Fact]
    public async Task UnknownRoute_SecretInQueryString_IsNotInTraceTags()
    {
        var secret = "trace-secret-" + Fixture.Create<Guid>().ToString("N");
        using var isolated = Factory.WithWebHostBuilder(_ => { });

        await AssertQueryStringIsNotInTraceTagsAsync(isolated, secret);
    }

    [Fact]
    public async Task UnknownRoute_SecretInQueryStringWithPackageRedactionDisabled_IsNotInTraceTags()
    {
        var secret = "trace-secret-" + Fixture.Create<Guid>().ToString("N");
        using var isolated = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "true"));

        await AssertQueryStringIsNotInTraceTagsAsync(isolated, secret);
    }

    [Fact]
    public async Task OutboundHttpClient_SecretInQueryString_IsNotInTraceTags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var secret = "outbound-secret-" + Fixture.Create<Guid>().ToString("N");
        var port = GetFreePort();
        var uniquePath = $"/probe/{Guid.NewGuid():N}";
        _ = Factory.Services;
        using var recorder = new ActivityRecorder(uniquePath, ActivityRecorder.HttpClientSource);

        await SendOutboundProbeAsync($"http://127.0.0.1:{port}{uniquePath}?token={secret}", recorder, cancellationToken);

        var targeted = Assert.Single(recorder.Targeted);
        Assert.Equal($"http://127.0.0.1:{port}{uniquePath}", targeted.Tags[TraceTagNames.UrlFull]);
        Assert.DoesNotContain(TraceTagNames.UrlQuery, targeted.Tags.Keys);
        Assert.False(recorder.AnyContains(secret), "A recorded activity contained the query string secret.");
    }

    [Fact]
    public async Task OutboundHttpClient_SecretInUserInfoAndQueryString_IsNotInTraceTags()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var userInfoSecret = "userinfo-secret-" + Fixture.Create<Guid>().ToString("N");
        var querySecret = "query-secret-" + Fixture.Create<Guid>().ToString("N");
        var port = GetFreePort();
        var uniquePath = $"/probe/{Guid.NewGuid():N}";
        _ = Factory.Services;
        using var recorder = new ActivityRecorder(uniquePath, ActivityRecorder.HttpClientSource);

        await SendOutboundProbeAsync($"http://probeuser:{userInfoSecret}@127.0.0.1:{port}{uniquePath}?token={querySecret}", recorder, cancellationToken);

        var targeted = Assert.Single(recorder.Targeted);
        Assert.Equal($"http://127.0.0.1:{port}{uniquePath}", targeted.Tags[TraceTagNames.UrlFull]);
        Assert.False(recorder.AnyContains(userInfoSecret), "A recorded activity contained the user info secret.");
        Assert.False(recorder.AnyContains(querySecret), "A recorded activity contained the query string secret.");
        Assert.False(recorder.AnyContains("probeuser"), "A recorded activity contained the user name.");
    }

    private static async Task SendOutboundProbeAsync(string url, ActivityRecorder recorder, CancellationToken cancellationToken)
    {
        using var outbound = new HttpClient(new SocketsHttpHandler());

        await Assert.ThrowsAnyAsync<HttpRequestException>(() => outbound.GetAsync(url, cancellationToken));

        await recorder.WaitForTargetedActivityAsync(cancellationToken);
    }

    private static int GetFreePort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        return ((IPEndPoint)probe.LocalEndpoint).Port;
    }

    private static async Task AssertQueryStringIsNotInTraceTagsAsync(WebApplicationFactory<Program> isolated, string secret)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = isolated.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await client.GetAsync("/alive", cancellationToken);
        var uniquePath = $"/does-not-exist/{Guid.NewGuid():N}";
        using var recorder = new ActivityRecorder(uniquePath, ActivityRecorder.AspNetCoreSource, ActivityRecorder.HttpClientSource);

        var response = await client.GetAsync($"{uniquePath}?token={secret}", cancellationToken);
        await recorder.WaitForTargetedActivityAsync(cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            recorder.Targeted,
            activity => activity.SourceName == ActivityRecorder.AspNetCoreSource && activity.Tags.ContainsKey(TraceTagNames.HttpRequestMethod));
        Assert.DoesNotContain(recorder.Targeted, activity => activity.Tags.ContainsKey(TraceTagNames.UrlQuery));
        Assert.False(recorder.AnyContains(secret), "A recorded activity contained the query string secret.");
    }

    [Fact]
    public async Task StartupAndHealthCalls_AzuriteConnectionString_IsNeverLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        Logs.Clear();
        using var fresh = Factory.WithWebHostBuilder(_ => { });
        using var client = fresh.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await client.GetAsync("/health", cancellationToken);
        await client.GetAsync("/alive", cancellationToken);

        Assert.NotEmpty(Logs.Entries);
        AssertLogsDoNotContain(AzuriteAccountKey, "AccountKey", Factory.AzuriteConnectionString);
    }

    [Fact]
    public async Task UnhandledException_TestUserHeaders_AreNotLogged()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var displayName = "name-" + Fixture.Create<Guid>().ToString("N");
        var user = TestUsers.AuthenticatedWithoutRole with { DisplayName = displayName };
        SetAuthenticatedClient(user);

        await Client.GetAsync("/does-not-exist", cancellationToken);

        AssertLogsDoNotContain(displayName);
    }
}
