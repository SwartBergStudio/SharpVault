// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Security.Cryptography;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Transfers;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Encryption;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferResilienceTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int TransientKeyVaultFailures = 2;
    private const int BlockedCycles = 5;
    private const string WrappedKeyColumn = "WrappedDataKey";
    private static readonly TimeSpan CappedBackoff = TimeSpan.FromSeconds(90);
    private static readonly int[] ExpectedBackoffSeconds = [30, 60, 90, 90];

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task Job_SourceRingUnreadable_BlocksAndMovesNothing_UntilTheRingIsRepaired()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var original = CopyOf(await ReadRingAsync(TestUsers.User.ObjectId));
        await VaultsTable.UpsertEntityAsync(WithScrambledWrappedKey(original), TableUpdateMode.Replace, Token);

        await Runner.RunOnceAsync(Token);

        var blocked = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.SourceVaultUnreadable, blocked.BlockReason);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);

        await VaultsTable.UpsertEntityAsync(original, TableUpdateMode.Replace, Token);
        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Job_TargetRingUnreadable_BlocksAndKeepsTheSource_UntilTheRingIsRepaired()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        Require(await ApiAs(TestUsers.OtherUser).CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Target own", Value = "own" }));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var original = CopyOf(await ReadRingAsync(TestUsers.OtherUser.ObjectId));
        await VaultsTable.UpsertEntityAsync(WithScrambledWrappedKey(original), TableUpdateMode.Replace, Token);

        await Runner.RunOnceAsync(Token);

        var blocked = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.TargetVaultUnreadable, blocked.BlockReason);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);
        Assert.Single(await ListAllSecretsAsync(TestUsers.OtherUser));

        await VaultsTable.UpsertEntityAsync(original, TableUpdateMode.Replace, Token);
        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count + 1, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Job_TargetRingStillUnreadable_BlockedRetriesWriteNoResumedOrBlockedRows()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        Require(await ApiAs(TestUsers.OtherUser).CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Target own", Value = "own" }));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var original = CopyOf(await ReadRingAsync(TestUsers.OtherUser.ObjectId));
        await VaultsTable.UpsertEntityAsync(WithScrambledWrappedKey(original), TableUpdateMode.Replace, Token);

        await Runner.RunOnceAsync(Token);
        for (var cycle = 0; cycle < BlockedCycles; cycle++)
        {
            clock.Advance(TransferLimits.BlockedRetryDelay + TimeSpan.FromSeconds(1));
            await Runner.RunOnceAsync(Token);
        }

        var blocked = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.TargetVaultUnreadable, blocked.BlockReason);
        Assert.Equal(1, await CountTransferAuditRowsAsync(transfer.Id, nameof(AuditEventType.TransferBlocked)));
        Assert.Equal(0, await CountTransferAuditRowsAsync(transfer.Id, nameof(AuditEventType.TransferResumed)));
    }

    [Fact]
    public async Task Job_RepeatedFailure_BacksOffDoublingAndCapsAtMaxBackoff()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var failing = 0;
        UseHost(FailingStagingHost(clock, () => Volatile.Read(ref failing) == 1));
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        Volatile.Write(ref failing, 1);

        for (var attempt = 0; attempt < ExpectedBackoffSeconds.Length; attempt++)
        {
            await Runner.RunOnceAsync(Token);
            var job = await StoredJobAsync(transfer.Id);
            Assert.Equal(attempt + 1, job.Attempts);
            Assert.NotNull(job.NextAttemptAt);
            Assert.Equal(TimeSpan.FromSeconds(ExpectedBackoffSeconds[attempt]), job.NextAttemptAt.Value - clock.GetUtcNow());
            clock.Advance(job.NextAttemptAt.Value - clock.GetUtcNow());
        }

        Volatile.Write(ref failing, 0);
        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Job_KeyVaultTransientFailure_RetriesWithBackoffAndLosesNothing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var budget = new KeyVaultFlakeBudget();
        UseHost(Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IEnvelopeEncryptor>(sp => new EnvelopeEncryptor(
                new FlakyKeyWrapProvider(sp.GetRequiredService<CountingKeyWrapProvider>(), budget),
                sp.GetRequiredService<ILogger<EnvelopeEncryptor>>())));
        })));
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 3, folderCount: 1);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        budget.Arm(TransientKeyVaultFailures);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransientKeyVaultFailures, budget.Consumed);
        Assert.True(
            Logs.Entries.Any(entry => entry.Message.Contains("step failed (RequestFailedException)", StringComparison.Ordinal)),
            "The transient Key Vault failure was never recorded as a failed step.");
        Assert.Equal(TransferStateDto.Completed, completed.State);
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(secret => secret.Id).ToList();
        Assert.Equal(seeded.Secrets.Count, targetIds.Count);
        Assert.Equal(targetIds.Count, targetIds.Distinct().Count());
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    private WebApplicationFactory<Program> FailingStagingHost(FakeTimeProvider clock, Func<bool> failing) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Transfers:MaxBackoff", CappedBackoff.ToString("c", CultureInfo.InvariantCulture));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(clock);
                services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
                    HookProxy.Wrap<ITransferStagingStore>(
                        ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp),
                        (_, _) => failing()
                            ? throw new InvalidOperationException("Simulated staging failure for the backoff test.")
                            : Task.CompletedTask)));
            });
        });

    private async Task<TransferJob> StoredJobAsync(Guid transferId)
    {
        var stored = await Host.Services.GetRequiredService<ITransferJobStore>().GetAsync(transferId, Token);
        return stored?.Value ?? throw new InvalidOperationException("The job row is missing.");
    }

    private async Task<TransferDto> ResumeUntilCompletedAsync(Guid transferId, FakeTimeProvider clock)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return current;
    }

    private async Task<int> CountTransferAuditRowsAsync(Guid transferId, string eventType)
    {
        var rows = await AuditRows.ListAsync(GetAuditTable(), Token);
        return rows.Count(row => row.GetString("EventType") == eventType
            && string.Equals(row.GetString("TransferId"), transferId.ToString("D"), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<TableEntity> ReadRingAsync(Guid owner)
    {
        var stored = await VaultsTable.GetEntityAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.KeyRingRowKey, cancellationToken: Token);
        return stored.Value;
    }

    private static TableEntity CopyOf(TableEntity source)
    {
        var copy = new TableEntity(source.PartitionKey, source.RowKey);
        foreach (var key in source.Keys.Where(key => key is not (nameof(TableEntity.PartitionKey) or nameof(TableEntity.RowKey) or nameof(TableEntity.Timestamp))))
        {
            copy[key] = source[key];
        }

        return copy;
    }

    private static TableEntity WithScrambledWrappedKey(TableEntity ring)
    {
        var scrambled = CopyOf(ring);
        var wrapped = (byte[])ring[WrappedKeyColumn];
        scrambled[WrappedKeyColumn] = RandomNumberGenerator.GetBytes(wrapped.Length);
        return scrambled;
    }
}
