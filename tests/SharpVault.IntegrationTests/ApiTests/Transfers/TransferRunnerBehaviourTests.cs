// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferRunnerBehaviourTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static readonly TimeSpan BackoffDelay = TimeSpan.FromSeconds(30);

    private static TestUser[] Users =>
    [
        TestUsers.Admin,
        TestUsers.SecondAdmin,
        TestUsers.User,
        TestUsers.OtherUser,
    ];

    [Fact]
    public async Task Expiry_PendingPastExpiresAt_BecomesExpiredOnTheNextRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(Users);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        clock.Advance(transfer.ExpiresAt - clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        await Runner.RunOnceAsync(Token);

        var expired = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        Assert.Equal(TransferStateDto.Expired, expired.State);
        Assert.Equal(HttpStatusCode.Created, (await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser))).Response.StatusCode);
    }

    [Fact]
    public async Task Expiry_ApprovedJobIsNotExpired()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        await WarmUpAllAsync(Users);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        clock.Advance(transfer.ExpiresAt - clock.GetUtcNow() + TimeSpan.FromMinutes(1));
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
    }

    [Fact]
    public async Task TwoRunners_SameTransfer_NoDuplicatesNoLoss()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var stageCrash = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: true);
        UseHost(CrashingTransferHost(stageCrash, clock));
        await WarmUpAllAsync(Users);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 4, folderCount: 1);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        stageCrash.Arm();
        await Runner.RunOnceAsync(Token);
        Assert.True(stageCrash.Fired, "The first runner never reached the staging crash point.");
        Assert.Equal(TransferStateDto.Staging, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
        clock.Advance(TimeSpan.FromHours(1));

        var takeover = ActivatorUtilities.CreateInstance<TransferJobRunner>(Host.Services);
        var takeoverResult = await takeover.RunOnceAsync(Token);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferRunResult.Ran, takeoverResult);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        var received = await ListAllSecretsAsync(TestUsers.OtherUser);
        Assert.Equal(seeded.Secrets.Count, received.Count);
        Assert.Equal(received.Count, received.Select(secret => secret.Id).Distinct().Count());
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
    }

    [Fact]
    public async Task Job_FailureBacksOff_IsNotRetriedBeforeItsNextAttempt()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new CrashPlan("IVaultStore.SubmitAsync", 1, afterEffect: false);
        UseHost(CrashingVaultHost(plan, clock));
        await WarmUpAllAsync(Users);
        await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        var immediately = await Runner.RunOnceAsync(Token);
        Assert.True(plan.Fired, "The crash point was never reached, so the case proves nothing.");
        Assert.Equal(TransferRunResult.Idle, immediately);

        clock.Advance(BackoffDelay + TimeSpan.FromSeconds(1));
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
        Assert.Equal(TransferStateDto.Completed, settled.State);
    }

    private WebApplicationFactory<Program> CrashingVaultHost(CrashPlan plan, FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IVaultStore>(sp =>
                CrashProxies.Wrap<IVaultStore>(ActivatorUtilities.CreateInstance<TableVaultStore>(sp), plan)));
        }));
}
