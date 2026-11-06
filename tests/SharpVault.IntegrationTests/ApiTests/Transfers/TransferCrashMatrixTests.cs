// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

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
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferCrashMatrixTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int SeededSecrets = 3;
    private const int SeededFolders = 2;

    [Theory]
    [InlineData("ITransferJobStore.ReplaceAsync", 1, false)]
    [InlineData("ITransferStagingStore.StageSecretsAsync", 1, false)]
    [InlineData("ITransferStagingStore.StageFoldersAsync", 1, false)]
    [InlineData("ITransferJobStore.ReplaceAsync", 3, false)]
    [InlineData("IVaultStore.SubmitAsync", 1, false)]
    [InlineData("IVaultStore.SubmitAsync", 2, true)]
    [InlineData("ITransferStagingStore.PublishAsync", 1, false)]
    [InlineData("ITransferStagingStore.PublishAsync", 2, true)]
    [InlineData("ITransferJobStore.ReplaceAsync", 5, false)]
    [InlineData("ITransferJobStore.CloseAsync", 1, false)]
    [InlineData("ITransferStagingStore.TakeFenceAsync", 1, false)]
    [InlineData("ITransferStagingStore.TakeFenceAsync", 1, true)]
    [InlineData("ITransferStagingStore.TakeFenceAsync", 2, false)]
    [InlineData("ITransferStagingStore.TakeFenceAsync", 2, true)]
    [InlineData("ITransferStagingStore.ReleaseFenceAsync", 1, false)]
    [InlineData("ITransferStagingStore.ReleaseFenceAsync", 1, true)]
    public async Task Job_CrashAtEveryPoint_ResumesWithoutLossOrDuplicates(string point, int occurrence, bool afterEffect)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan(point, occurrence, afterEffect);
        UseHost(CrashingHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecrets, SeededFolders);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        var current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
            await AssertAtRestAsync(current.State);
        }

        Assert.True(plan.Fired, "The crash point was never reached, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Completed, current.State);
        await AssertMovedExactlyOnceAsync(seeded, transfer.Id);
    }

    [Fact]
    public async Task Job_LargeTransferFailureAtThirdPublish_ResumesAndMovesEveryItemOnce()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("ITransferStagingStore.PublishAsync", 3, afterEffect: false);
        UseHost(CrashingHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 120, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        var current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        }

        Assert.True(plan.Fired, "The third publish call was never reached.");
        Assert.Equal(TransferStateDto.Completed, current.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id).Distinct().Count());
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Staging_StagedItemsNeverVisibleToTheTargetBeforeRelease()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("IVaultStore.SubmitAsync", 1, afterEffect: false);
        UseHost(CrashingHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, SeededSecrets, SeededFolders);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        var current = await GetTransferAsync(TestUsers.Admin, transfer.Id);

        Assert.True(plan.Fired);
        Assert.Equal(TransferStateDto.Releasing, current.State);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
        Assert.Equal(SeededSecrets, (await ListAllSecretsAsync(TestUsers.User)).Count);
    }

    [Fact]
    public async Task Staging_StagedRowsExistUntilPublish_TargetListsNothing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("IVaultStore.SubmitAsync", 1, afterEffect: false);
        UseHost(CrashingHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, SeededSecrets, SeededFolders);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        Assert.True(plan.Fired);
        Assert.True(await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id) > 0, "No staged rows were written before the crash.");
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    private WebApplicationFactory<Program> CrashingHost(CrashPlan plan, FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IVaultStore>(sp =>
                CrashProxies.Wrap<IVaultStore>(ActivatorUtilities.CreateInstance<TableVaultStore>(sp), plan)));
            services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
                CrashProxies.Wrap<ITransferStagingStore>(ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp), plan)));
            services.Replace(ServiceDescriptor.Singleton<ITransferJobStore>(sp =>
                CrashProxies.Wrap<ITransferJobStore>(ActivatorUtilities.CreateInstance<TableTransferJobStore>(sp), plan)));
        }));

    private async Task AssertAtRestAsync(TransferStateDto state)
    {
        var sourceIds = (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id).ToHashSet();
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id).ToHashSet();

        Assert.Empty(sourceIds.Intersect(targetIds));
        Assert.True(sourceIds.Count + targetIds.Count <= SeededSecrets, "An item is visible in two vaults.");
        if (state is TransferStateDto.Approved or TransferStateDto.Staging or TransferStateDto.Blocked or TransferStateDto.Releasing)
        {
            Assert.Empty(targetIds);
        }
    }

    private async Task AssertMovedExactlyOnceAsync(SeededVault seeded, Guid transferId)
    {
        var moved = await ListAllSecretsAsync(TestUsers.OtherUser);
        Assert.Equal(seeded.Secrets.Count, moved.Count);
        Assert.Equal(seeded.Secrets.Select(secret => secret.Id).ToHashSet(), moved.Select(item => item.Id).ToHashSet());
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transferId));

        foreach (var secret in seeded.Secrets)
        {
            Assert.Equal(secret.Value, await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
        }

        var folders = await ListFoldersForAsync(TestUsers.OtherUser);
        Assert.Equal(seeded.Folders.Count + 1, folders.Count);
        Assert.Equal(seeded.Folders.Count + 1, folders.Select(folder => folder.Id).Distinct().Count());
    }

    private async Task<IReadOnlyList<FolderDto>> ListFoldersForAsync(TestUser owner)
    {
        var (response, body) = await ApiAs(owner).ListFoldersAsync();
        Assert.True(response.IsSuccessStatusCode, $"Folder list failed with {response.StatusCode}.");
        return body?.Items ?? [];
    }
}
