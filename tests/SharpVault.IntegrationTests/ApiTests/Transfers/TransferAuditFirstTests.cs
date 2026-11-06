// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Transfers;
using SharpVault.Infrastructure.Audit;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferAuditFirstTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    [Fact]
    public async Task Job_ApprovalAuditFails_MovesNothing()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new AuditEventCrashPlan(AuditEventType.TransferApproved);
        UseHost(CrashingAuditHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        var afterFailure = await GetTransferAsync(TestUsers.Admin, transfer.Id);

        Assert.True(plan.Fired, "The approval audit was never written, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Approved, afterFailure.State);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
    }

    [Fact]
    public async Task Job_ItemAuditFails_DoesNotPublishTheItem()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new AuditEventCrashPlan(AuditEventType.SecretTransferred);
        UseHost(CrashingAuditHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        var afterFailure = await GetTransferAsync(TestUsers.Admin, transfer.Id);

        Assert.True(plan.Fired, "The item audit was never written, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Publishing, afterFailure.State);
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.SecretTransferred));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Equal(seeded.Secrets.Count, (await VaultAuditRowsAsync(AuditEventType.SecretTransferred)).Count);
    }

    [Fact]
    public async Task Job_ReleaseStartAuditFails_IsRetriedOnTheNextRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var plan = new AuditEventCrashPlan(AuditEventType.TransferReleaseStarted);
        UseHost(CrashingAuditHost(plan, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        await Runner.RunOnceAsync(Token);
        var afterFailure = await GetTransferAsync(TestUsers.Admin, transfer.Id);

        Assert.True(plan.Fired, "The release start audit was never written, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Staging, afterFailure.State);
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.TransferReleaseStarted));
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));

        var completed = await ResumeUntilCompletedAsync(transfer.Id, clock);

        Assert.Equal(TransferStateDto.Completed, completed.State);
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.TransferReleaseStarted));
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
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

    private WebApplicationFactory<Program> CrashingAuditHost(AuditEventCrashPlan plan, FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IAuditLog>(sp =>
                HookProxy.Wrap<IAuditLog>(
                    ActivatorUtilities.CreateInstance<TableAuditLog>(sp),
                    (_, args) => plan.BeforeAppendAsync(args))));
        }));
}
