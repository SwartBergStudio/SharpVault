// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Headers;
using AutoFixture;
using Microsoft.AspNetCore.Mvc.Testing;
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
