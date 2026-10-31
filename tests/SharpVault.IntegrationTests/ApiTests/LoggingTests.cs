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
        using var isolated = Factory.WithWebHostBuilder(_ => { });
        using var hostClient = isolated.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        await hostClient.GetAsync("/alive", cancellationToken);
        var port = GetFreePort();
        var captured = new System.Collections.Concurrent.ConcurrentBag<string>();
        var activityCount = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                Interlocked.Increment(ref activityCount);
                captured.Add(activity.DisplayName);
                foreach (var tag in activity.TagObjects)
                {
                    captured.Add($"{tag.Key}={tag.Value}");
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        using var outbound = new HttpClient(new SocketsHttpHandler());

        await Assert.ThrowsAsync<HttpRequestException>(
            () => outbound.GetAsync($"http://127.0.0.1:{port}/probe?token={secret}", cancellationToken));

        Assert.True(activityCount > 0, "No outbound activity was captured, so the check would pass vacuously.");
        Assert.Contains($"url.full=http://127.0.0.1:{port}/probe", captured);
        Assert.DoesNotContain(captured, value => value.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(captured, value => value.StartsWith("url.query=", StringComparison.Ordinal));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25, cancellationToken);
        }
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
        var captured = new System.Collections.Concurrent.ConcurrentBag<string>();
        var activityCount = 0;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name is "Microsoft.AspNetCore" or "System.Net.Http",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                Interlocked.Increment(ref activityCount);
                captured.Add(activity.DisplayName);
                foreach (var tag in activity.TagObjects)
                {
                    captured.Add($"{tag.Key}={tag.Value}");
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        await client.GetAsync($"/does-not-exist?token={secret}", cancellationToken);
        await WaitUntilAsync(() => Volatile.Read(ref activityCount) > 0, cancellationToken);

        Assert.True(activityCount > 0, "No activity was captured, so the check would pass vacuously.");
        Assert.Contains(captured, value => value.StartsWith("http.request.method=", StringComparison.Ordinal));
        Assert.DoesNotContain(captured, value => value.Contains(secret, StringComparison.Ordinal));
        Assert.DoesNotContain(captured, value => value.StartsWith("url.query=", StringComparison.Ordinal));
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
