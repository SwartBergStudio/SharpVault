// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Time.Testing;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferRunnerIsolationTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    [Fact]
    public async Task OneFailingJob_DoesNotBlockTheOtherJobInTheSameRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: false);
        UseHost(CrashingTransferHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser, TestUsers.ShareViewer, TestUsers.ShareEditor);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        await SeedVaultAsync(TestUsers.ShareViewer, secretCount: 1, folderCount: 0);
        var failing = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        clock.Advance(TimeSpan.FromMinutes(1));
        var healthy = await RequestAsync(TestUsers.Admin, TestUsers.ShareViewer, TestUsers.ShareEditor);
        await ApproveAsync(TestUsers.SecondAdmin, failing);
        await ApproveAsync(TestUsers.SecondAdmin, healthy);
        plan.Arm();

        await Runner.RunOnceAsync(Token);

        Assert.True(plan.Fired, "The staging write was never reached, so the case proves nothing.");
        Assert.NotEqual(TransferStateDto.Completed, (await GetTransferAsync(TestUsers.Admin, failing.Id)).State);
        Assert.NotEqual(TransferStateDto.Approved, (await GetTransferAsync(TestUsers.Admin, healthy.Id)).State);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, healthy.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Single(await ListAllSecretsAsync(TestUsers.ShareEditor));
        Assert.NotEqual(TransferStateDto.Completed, (await GetTransferAsync(TestUsers.Admin, failing.Id)).State);
    }
}
