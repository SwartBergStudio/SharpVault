// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkRaceTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private const int ParallelOpens = 20;

    [Fact]
    public async Task OpenAndPurge_AtTheExpiryBoundary_RefusesEveryOpenAndPurgeDeletesTheExpiredRow()
    {
        var secret = await NewSecretAsync();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var linkId = await CreateLinkThroughAsync(host, secret.Id, lifetimeMinutes: 5);
        var expiresAt = await GetLinkExpiryAsync(host, linkId);
        clock.SetUtcNow(expiresAt);

        var purging = host.Services.GetRequiredService<IOneTimeLinkPurger>().PurgeExpiredAsync(Token);
        var opens = Enumerable.Range(0, ParallelOpens).Select(_ => OpenAtAsync(host, linkId, clock.GetUtcNow())).ToList();
        var responses = await Task.WhenAll(opens);
        await purging;

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
            Assert.Null(await host.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(linkId, Token));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task PurgeBeforeExpiry_LinkExpiringLaterInTheSameHour_SurvivesAndStillOpens()
    {
        var secret = await NewSecretAsync();
        var hourStart = FloorToHour(DateTimeOffset.UtcNow);
        var clock = new FakeTimeProvider(hourStart + TimeSpan.FromMinutes(10));
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));
        var linkId = await CreateLinkThroughAsync(host, secret.Id, lifetimeMinutes: 30);
        var expiresAt = await GetLinkExpiryAsync(host, linkId);
        Assert.Equal(hourStart, FloorToHour(expiresAt));
        clock.SetUtcNow(expiresAt - TimeSpan.FromMinutes(5));

        await host.Services.GetRequiredService<IOneTimeLinkPurger>().PurgeExpiredAsync(Token);

        Assert.NotNull(await host.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(linkId, Token));
        using var response = await OpenAtAsync(host, linkId, clock.GetUtcNow());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<Guid> CreateLinkThroughAsync(WebApplicationFactory<Program> host, Guid secretId, int lifetimeMinutes)
    {
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secretId),
            new CreateOneTimeLinkRequestDto { Payload = NewPayload(out _), LifetimeMinutes = lifetimeMinutes },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(body);
        return body.Id;
    }

    private static async Task<DateTimeOffset> GetLinkExpiryAsync(WebApplicationFactory<Program> host, Guid linkId)
    {
        var stored = await host.Services.GetRequiredService<IOneTimeLinkStore>().GetAsync(linkId, Token);
        Assert.NotNull(stored);
        return stored.Value.ExpiresAt;
    }

    private static DateTimeOffset FloorToHour(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerHour, TimeSpan.Zero);

    private static async Task<HttpResponseMessage> OpenAtAsync(WebApplicationFactory<Program> host, Guid linkId, DateTimeOffset at)
    {
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, at);
        using var client = CreateClientFor(host);
        return await client.SendAsync(request, Token);
    }
}
