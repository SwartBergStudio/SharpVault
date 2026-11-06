// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferStaleStageTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string StagedValue = "value-staged-before-the-takeover";
    private const string EditedValue = "value-edited-while-the-takeover-ran";
    private const string SecretName = "Stale stage subject";
    private const int SeededSecretCount = 3;

    [Fact]
    public async Task StaleStageWrite_AfterAnotherRunnerCompleted_LeavesNoStagedRow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transferId = Guid.Empty;
        var takeover = new FirstStageWrite(async () =>
        {
            clock.Advance(TimeSpan.FromMinutes(3));
            await RunTakeoverUntilAsync(clock, async () => (await GetTransferAsync(TestUsers.Admin, transferId)).State == TransferStateDto.Completed);
        });
        UseHost(Rig(clock, takeover.Invoke));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        transferId = transfer.Id;
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.True(takeover.Fired, "The stage write was never reached, so the stale runner was not exercised.");
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transferId));
        Assert.Equal(SeededSecretCount, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
    }

    [Fact]
    public async Task StaleStageWrite_WhileAnotherRunnerPublishes_TargetGetsEachSecretOnce()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publishCrash = new CrashPlan("ITransferStagingStore.PublishAsync", 2, afterEffect: true);
        var takeover = new FirstStageWrite(async () =>
        {
            publishCrash.Arm();
            clock.Advance(TimeSpan.FromMinutes(3));
            await RunTakeoverUntilAsync(clock, () => Task.FromResult(publishCrash.Fired));
        });
        UseHost(Rig(clock, takeover.Invoke, publishCrash));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);
        clock.Advance(TimeSpan.FromHours(1));
        var settled = await ResumeUntilAsync(clock, transfer.Id, TransferStateDto.Completed);

        Assert.True(takeover.Fired, "The stage write was never reached, so the stale runner was not exercised.");
        Assert.True(publishCrash.Fired, "The publish crash never fired, so the other runner never reached publish.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(SeededSecretCount, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Equal(0, await CountAuditEventsAsync(AuditEventType.SecretTransferredUnderNewId, transfer.Id));
        Assert.Equal(0, settled.RenamedItemCount);
    }

    [Fact]
    public async Task StaleStageWrite_OwnerEditedInBetween_TargetGetsTheEditedValue()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var publishCrash = new CrashPlan("ITransferStagingStore.PublishAsync", 2, afterEffect: false);
        var secretId = Guid.Empty;
        var takeover = new FirstStageWrite(async () =>
        {
            await ReplaceValueAsync(TestUsers.User, secretId, EditedValue);
            publishCrash.Arm();
            clock.Advance(TimeSpan.FromMinutes(3));
            await RunTakeoverUntilAsync(clock, () => Task.FromResult(publishCrash.Fired));
        });
        UseHost(Rig(clock, takeover.Invoke, publishCrash));
        await WarmUpAllAsync(AllParticipants);
        secretId = (await CreateSecretAsync(TestUsers.User, SecretName, StagedValue)).Id;
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);
        clock.Advance(TimeSpan.FromHours(1));
        var settled = await ResumeUntilAsync(clock, transfer.Id, TransferStateDto.Completed);

        Assert.True(takeover.Fired, "The stage write was never reached, so the stale runner was not exercised.");
        Assert.True(publishCrash.Fired, "The publish crash never fired, so the other runner never reached publish.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(EditedValue, await RevealAsync(TestUsers.OtherUser, secretId, RevealFieldDto.Value));
    }

    [Fact]
    public async Task StaleStageWrite_AfterAbortCleanup_LeavesNoStagedRow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transferId = Guid.Empty;
        var takeover = new FirstStageWrite(async () =>
        {
            await AbortAsync(transferId);
            clock.Advance(TimeSpan.FromMinutes(3));
            await RunTakeoverUntilAsync(clock, () => FenceRowAbsentAsync(transferId));
        });
        UseHost(Rig(clock, takeover.Invoke));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        transferId = transfer.Id;
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.True(takeover.Fired, "The stage write was never reached, so the stale runner was not exercised.");
        Assert.Equal(TransferStateDto.Aborted, (await GetTransferAsync(TestUsers.Admin, transferId)).State);
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transferId));
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transferId));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task FenceStolenMidStage_RunnerStopsThenNextRunCompletes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transferId = Guid.Empty;
        var thief = new FirstStageWrite(() => StealFenceAsync(transferId), nameof(ITransferStagingStore.StageSecretsAsync));
        UseHost(Rig(clock, thief.Invoke));
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        transferId = transfer.Id;
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.True(thief.Fired, "The stage write was never reached, so no fence was stolen.");
        Assert.Contains(Logs.Entries, entry => entry.Message.Contains("was changed by another request", StringComparison.Ordinal));
        var settled = await ResumeUntilAsync(clock, transferId, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(0, settled.RenamedItemCount);
        var moved = await ListAllSecretsAsync(TestUsers.OtherUser);
        Assert.Equal(seeded.Secrets.Count, moved.Count);
        Assert.Equal(seeded.Secrets.Count, moved.Select(item => item.Id).Distinct().Count());
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transferId));
    }

    [Fact]
    public async Task StaleRunner_ConcurrentAbortChangesTheJob_FailsOnItsJobSave_AndTheCleanupFinishes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transferId = Guid.Empty;
        var concurrentAbort = new FirstStageWrite(() => AbortAsync(transferId), nameof(ITransferStagingStore.ListIndexAsync));
        UseHost(Rig(clock, concurrentAbort.Invoke));
        await WarmUpAllAsync(AllParticipants);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        transferId = transfer.Id;
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.True(concurrentAbort.Fired, "The index read was never reached, so the stale runner never took the fence.");
        Assert.Contains(Logs.Entries, entry => entry.Message.Contains("was changed by another request", StringComparison.Ordinal));
        for (var run = 0; run < 2; run++)
        {
            clock.Advance(TimeSpan.FromHours(1));
            await Runner.RunOnceAsync(Token);
        }

        Assert.Equal(TransferStateDto.Aborted, (await GetTransferAsync(TestUsers.Admin, transferId)).State);
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transferId));
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transferId));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);
    }

    [Fact]
    public async Task Completed_LeavesNoFenceRow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var counts = new ConcurrentDictionary<string, int>();
        UseHost(Rig(clock, (method, args) => CountCall(counts, method, args)));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 2);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await ResumeUntilAsync(clock, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.True(CallCount(counts, nameof(ITransferStagingStore.TakeFenceAsync)) > 0, "No fence was ever taken, so the case proves nothing.");
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
    }

    [Fact]
    public async Task Aborted_LeavesNoFenceRow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var counts = new ConcurrentDictionary<string, int>();
        UseHost(Rig(clock, (method, args) => CountCall(counts, method, args)));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await AbortAsync(transfer.Id);

        await Runner.RunOnceAsync(Token);

        Assert.Equal(TransferStateDto.Aborted, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
        Assert.True(CallCount(counts, nameof(ITransferStagingStore.TakeFenceAsync)) > 0, "The cleanup never took the fence, so the case proves nothing.");
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
    }

    [Fact]
    public async Task StagePass_TakesTheFenceOnce()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var calls = new ConcurrentQueue<string>();
        UseHost(Rig(clock, (method, args) =>
        {
            calls.Enqueue(method.Name);
            return Task.CompletedTask;
        }));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 2);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await ResumeUntilAsync(clock, transfer.Id, TransferStateDto.Completed);

        var sequence = calls.ToArray();
        var firstSecrets = Array.IndexOf(sequence, nameof(ITransferStagingStore.StageSecretsAsync));
        var firstFolders = Array.IndexOf(sequence, nameof(ITransferStagingStore.StageFoldersAsync), Math.Max(firstSecrets, 0));
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.True(firstSecrets > 0 && firstFolders > firstSecrets, "The pass did not write both secrets and folders, so the case proves nothing.");
        Assert.Contains(sequence[..firstSecrets], name => name == nameof(ITransferStagingStore.TakeFenceAsync));
        Assert.DoesNotContain(sequence[(firstSecrets + 1)..firstFolders], name => name == nameof(ITransferStagingStore.TakeFenceAsync));
    }

    [Fact]
    public async Task StagePass_FenceTakenThenJobChanged_ReleasesTheFenceRow()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var transferId = Guid.Empty;
        var fired = 0;
        UseHost(Rig(clock, async (method, _) =>
        {
            if (method.Name == nameof(ITransferStagingStore.TakeFenceAsync) && Interlocked.Exchange(ref fired, 1) == 0)
            {
                await GetTableClient(TableNames.TransferJobs).UpsertEntityAsync(
                    new TableEntity(TransferKeys.TransferPartition, TransferKeys.JobRowKey(transferId)) { ["Touched"] = true },
                    TableUpdateMode.Merge,
                    cancellationToken: Token);
            }
        }));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, SeededSecretCount, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        transferId = transfer.Id;
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);

        Assert.Equal(1, fired);
        Assert.False(await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transferId));
    }

    private async Task<bool> FenceRowExistsAsync(Guid owner, Guid transferId) =>
        (await VaultsTable.GetEntityIfExistsAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.StagingFenceRowKey(transferId), cancellationToken: Token)).HasValue;

    private static TestUser[] AllParticipants =>[TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    private const string AbortReason = "Aborted while the stale runner stages";

    private static Task CountCall(ConcurrentDictionary<string, int> counts, MethodInfo method, object?[]? args)
    {
        counts.AddOrUpdate(method.Name, 1, (_, count) => count + 1);
        return Task.CompletedTask;
    }

    private static int CallCount(ConcurrentDictionary<string, int> counts, string method) => counts.GetValueOrDefault(method);

    private async Task StealFenceAsync(Guid transferId)
    {
        using var scope = Factory.Services.CreateScope();
        var raw = ActivatorUtilities.CreateInstance<TableTransferStagingStore>(scope.ServiceProvider);
        await raw.TakeFenceAsync(TestUsers.OtherUser.ObjectId, transferId, Token);
    }

    private async Task AbortAsync(Guid transferId)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        var (response, _) = await ApiAs(TestUsers.Admin).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transferId)),
            new TransferDecisionRequestDto { Reason = AbortReason },
            ifMatch: current.ConcurrencyToken);
        Assert.True(response.IsSuccessStatusCode, $"Abort failed with {response.StatusCode}.");
    }

    private async Task<bool> FenceRowAbsentAsync(Guid transferId) =>
        !await FenceRowExistsAsync(TestUsers.OtherUser.ObjectId, transferId);

    private WebApplicationFactory<Program> Rig(FakeTimeProvider clock, Func<MethodInfo, object?[]?, Task> hook, params CrashPlan[] stagingPlans) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
            {
                ITransferStagingStore store = ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp);
                foreach (var plan in stagingPlans)
                {
                    store = CrashProxies.Wrap(store, plan);
                }

                return HookProxy.Wrap(store, hook);
            }));
        }));

    private async Task RunTakeoverUntilAsync(FakeTimeProvider clock, Func<Task<bool>> isDone)
    {
        var takeover = ActivatorUtilities.CreateInstance<TransferJobRunner>(Host.Services);
        for (var run = 0; run < RunBound && !await isDone(); run++)
        {
            await takeover.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
        }
    }

    private async Task<TransferDto> ResumeUntilAsync(FakeTimeProvider clock, Guid transferId, TransferStateDto state)
    {
        var current = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < RunBound && current.State != state; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return current;
    }

    private async Task<SecretDto> CreateSecretAsync(TestUser owner, string name, string value)
    {
        var body = new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = name, Value = value };
        var (response, secret) = await ApiAs(owner).CreateSecretAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return secret ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task ReplaceValueAsync(TestUser owner, Guid secretId, string value)
    {
        var (_, current) = await ApiAs(owner).GetSecretAsync(secretId);
        Assert.NotNull(current);
        var body = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = current.Name ?? string.Empty, ReplaceValue = true, Value = value };
        var (response, _) = await ApiAs(owner).UpdateSecretAsync(secretId, current.ConcurrencyToken, body);
        Assert.True(response.IsSuccessStatusCode, $"Edit failed with {response.StatusCode}.");
    }

    private sealed class FirstStageWrite(Func<Task> action, string methodName = nameof(ITransferStagingStore.StageSecretsAsync))
    {
        private int _claimed;

        public bool Fired => Volatile.Read(ref _claimed) == 1;

        public async Task Invoke(MethodInfo method, object?[]? args)
        {
            if (method.Name == methodName && Interlocked.Exchange(ref _claimed, 1) == 0)
            {
                await action();
            }
        }
    }
}
