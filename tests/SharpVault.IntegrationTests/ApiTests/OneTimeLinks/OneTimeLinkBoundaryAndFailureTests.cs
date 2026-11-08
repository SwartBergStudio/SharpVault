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
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.Infrastructure.Audit;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.OneTimeLinks;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OneTimeLinkBoundaryAndFailureTests(ApiTestFactory factory) : OneTimeLinkTestBase(factory)
{
    private static readonly TimeSpan TestLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OneTick = TimeSpan.FromTicks(1);

    [Fact]
    public async Task Open_OneTickBeforeExpiry_Succeeds()
    {
        var secret = await NewSecretAsync();
        var payload = NewPayload(out _);
        var timed = await CreateTimedLinkAsync(secret.Id, payload);

        timed.Clock.SetUtcNow(timed.ExpiresAt - OneTick);
        using var response = await OpenThroughAsync(timed.Host, timed.LinkId, timed.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OneTimeLinkPayloadDto>(Token);
        Assert.Equal(payload, body?.Payload);
    }

    [Fact]
    public async Task Open_AtExactExpiry_ReturnsGeneric404()
    {
        var secret = await NewSecretAsync();
        var timed = await CreateTimedLinkAsync(secret.Id, NewPayload(out _));

        timed.Clock.SetUtcNow(timed.ExpiresAt);
        using var response = await OpenThroughAsync(timed.Host, timed.LinkId, timed.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Open_OneTickPastExpiry_ReturnsGeneric404()
    {
        var secret = await NewSecretAsync();
        var timed = await CreateTimedLinkAsync(secret.Id, NewPayload(out _));

        timed.Clock.SetUtcNow(timed.ExpiresAt + OneTick);
        using var response = await OpenThroughAsync(timed.Host, timed.LinkId, timed.Clock.GetUtcNow());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Open_AuditFails_ReturnsNoPayload()
    {
        var secret = await NewSecretAsync();
        var payload = NewPayload(out var marker);
        var linkId = await CreateLinkAsync(secret.Id, payload);
        var plan = new AuditEventCrashPlan(AuditEventType.OneTimeLinkOpened);
        using var host = AuditCrashHost(plan);
        plan.Arm();

        using var response = await OpenThroughAsync(host, linkId, DateTimeOffset.UtcNow);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.True(plan.Fired);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain(payload, body, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_AuditFails_ReturnsNoPlaintext()
    {
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(OwnerVault, markers);
        var plan = new AuditEventCrashPlan(AuditEventType.OneTimeLinkSnapshotRevealed);
        using var host = AuditCrashHost(plan);
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        plan.Arm();

        using var response = await client.PostAsync(SecretRoute(ApiRoutes.OneTimeLinkSecretSnapshot, OwnerId, secret.Id), null, Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.True(plan.Fired);
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(markers.Value, body, StringComparison.Ordinal);
        Assert.DoesNotContain(markers.Notes, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenAndRevoke_Race_ExactlyOneOutcomeIsAudited()
    {
        for (var round = 0; round < 5; round++)
        {
            var secret = await NewSecretAsync();
            var linkId = await CreateLinkAsync(secret.Id, NewPayload(out _));

            var opened = OpenAsync(linkId);
            var revoked = RevokeAsync(Owner, linkId);
            using var openResponse = await opened;
            using var revokeResponse = await revoked;

            var openWon = openResponse.StatusCode == HttpStatusCode.OK;
            var revokeWon = revokeResponse.StatusCode == HttpStatusCode.NoContent;
            Assert.True(openWon ^ revokeWon, $"Round {round}: open {(int)openResponse.StatusCode}, revoke {(int)revokeResponse.StatusCode}.");

            var openedRows = await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkOpened, linkId);
            var revokedRows = await AuditRowsMentioningAsync(AuditEventType.OneTimeLinkRevoked, linkId);
            Assert.Equal(openWon ? 1 : 0, openedRows.Count);
            Assert.Equal(revokeWon ? 1 : 0, revokedRows.Count);
        }
    }

    private sealed record TimedLink(Guid LinkId, DateTimeOffset ExpiresAt, FakeTimeProvider Clock, WebApplicationFactory<Program> Host);

    private async Task<TimedLink> CreateTimedLinkAsync(Guid secretId, string payload)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var host = TimedHost(clock);
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        using var response = await client.PostAsJsonAsync(
            SecretRoute(ApiRoutes.OneTimeLinkSecret, OwnerId, secretId),
            new CreateOneTimeLinkRequestDto { Payload = payload, LifetimeMinutes = (int)TestLifetime.TotalMinutes },
            Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<OneTimeLinkDto>(Token);
        Assert.NotNull(created);
        return new TimedLink(created.Id, created.ExpiresAt, clock, host);
    }

    private WebApplicationFactory<Program> TimedHost(FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
        }));

    private WebApplicationFactory<Program> AuditCrashHost(AuditEventCrashPlan plan) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<IAuditLog>(sp =>
                HookProxy.Wrap<IAuditLog>(
                    ActivatorUtilities.CreateInstance<TableAuditLog>(sp),
                    (_, args) => plan.BeforeAppendAsync(args))))));

    private static async Task<HttpResponseMessage> OpenThroughAsync(WebApplicationFactory<Program> host, Guid linkId, DateTimeOffset at)
    {
        var path = OpenRoute(linkId);
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        TestWebGateway.AddLinkOpenProof(request, path, TestAddresses.Browser, OpenAgent, at);
        using var client = CreateClientFor(host);
        return await client.SendAsync(request, Token);
    }
}
