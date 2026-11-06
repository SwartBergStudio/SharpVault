// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Transfers;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
public sealed class TransferOversizeAndLeaseTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private WebApplicationFactory<Program> OversizeHost(OversizeTarget target) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
                OversizeStagingProxies.Wrap(ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp), target)))));

    private const int CjkCodePoint = 0x4E00;
    private const int MaxFieldCharacters = VaultLimits.ValueMaxLength;
    private const int WritesForFullHistory = VaultLimits.HistoryVersionLimit + 1;
    private const string StagingIndexField = "IndexColumns";

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task OversizeSecret_StaysWithTheSource_AndAuditsOneSkip()
    {
        var target = new OversizeTarget();
        UseHost(OversizeHost(target));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        var oversized = await CreateFullHistorySecretAsync(api, "Oversized");
        target.SecretId = oversized.Id;
        var healthy = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Healthy", Value = "small" }));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var sourceIds = (await ListAllSecretsAsync(TestUsers.User)).Select(secret => secret.Id).ToHashSet();
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(secret => secret.Id).ToHashSet();
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Contains(oversized.Id, sourceIds);
        Assert.DoesNotContain(oversized.Id, targetIds);
        Assert.Contains(healthy.Id, targetIds);
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.TransferItemSkipped, oversized.Id));
    }

    [Fact]
    public async Task LeaseHeldByAnotherRunner_IsSkipped_UntilTheLeaseExpires()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, 2, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var leases = Host.Services.GetRequiredService<IMaintenanceLeaseStore>();
        var heldByOther = await leases.TryAcquireAsync(TransferJobRunner.LeaseName, "another-runner", TimeSpan.FromMinutes(2), Token);
        Assert.NotNull(heldByOther);

        var whileHeld = await Runner.RunOnceAsync(Token);
        var movedWhileHeld = (await ListAllSecretsAsync(TestUsers.OtherUser)).Count;
        clock.Advance(TimeSpan.FromMinutes(3));
        var afterExpiry = await Runner.RunOnceAsync(Token);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferRunResult.Skipped, whileHeld);
        Assert.Equal(0, movedWhileHeld);
        Assert.Equal(TransferRunResult.Ran, afterExpiry);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
    }

    [Fact]
    public void StagingIndexProjection_SelectsNoCiphertextColumns()
    {
        var field = typeof(TableTransferStagingStore).GetField(StagingIndexField, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        var columns = Assert.IsAssignableFrom<IEnumerable<string>>(field.GetValue(null)).ToArray();

        Assert.NotEmpty(columns);
        Assert.DoesNotContain(columns, column => PayloadColumnMarkers.Any(marker => column.Contains(marker, StringComparison.OrdinalIgnoreCase)));
    }

    private static readonly string[] PayloadColumnMarkers = ["Envelope", "Ciphertext", "Nonce", "Notes", "Value", "Wrapped", "Binary"];

    private static async Task<SecretDto> CreateFullHistorySecretAsync(VaultTestApi api, string name)
    {
        var created = Require(await api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = name,
            Value = LargeText(0),
            Notes = LargeText(1),
        }));
        var current = created;
        for (var write = 1; write < WritesForFullHistory; write++)
        {
            var (response, body) = await api.UpdateSecretAsync(current.Id, current.ConcurrencyToken, new UpdateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = name,
                ReplaceValue = true,
                Value = LargeText(write * 2),
                ReplaceNotes = true,
                Notes = LargeText((write * 2) + 1),
            });
            Assert.True(response.IsSuccessStatusCode, $"Write {write} failed with {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            current = body ?? throw new InvalidOperationException("The update response had no body.");
        }

        return current;
    }

    private static string LargeText(int seed) => new((char)(CjkCodePoint + (seed % 256)), MaxFieldCharacters);
}
