// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Infrastructure.Vaults;
using SharpVault.Domain.Users;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;
using Microsoft.Extensions.Time.Testing;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferJobTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int SeededSecrets = 3;

    private async Task<int> SecretRowCountAsync(Guid owner)
    {
        var lower = VaultKeys.SecretPrefix;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {VaultKeys.PartitionKey(owner)} and RowKey ge {lower} and RowKey lt {VaultKeys.UpperBound(lower)}");
        var count = 0;
        await foreach (var _ in VaultsTable.QueryAsync<TableEntity>(filter, select: ["PartitionKey"], cancellationToken: Token))
        {
            count++;
        }

        return count;
    }

    private static bool IsBlockedOrDone(TransferDto transfer) =>
        transfer.State is TransferStateDto.Blocked or TransferStateDto.Completed;

    [Fact]
    public async Task Job_ApproverLockedBeforePreflight_BlocksAndMovesNothing()
    {
        var (transfer, _) = await ArrangeApprovedAsync();
        await SetUserStatusAsync(TestUsers.SecondAdmin.ObjectId, UserStatus.Locked);

        var blocked = await RunUntilAsync(TestUsers.Admin, transfer.Id, IsBlockedOrDone);

        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.ApproverNoLongerAdmin, blocked.BlockReason);
        Assert.Equal(SeededSecrets, (await ListAllSecretsAsync(TestUsers.User)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task Job_RequesterDemotedBeforePreflight_BlocksAndMovesNothing()
    {
        var (transfer, _) = await ArrangeApprovedAsync();
        await WarmUpUserAsync(TestUsers.Admin with { Roles = [UserRole] });

        var blocked = await RunUntilAsync(TestUsers.SecondAdmin, transfer.Id, IsBlockedOrDone);

        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.RequesterNoLongerAdmin, blocked.BlockReason);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task Job_TargetLockedBeforeGate_BlocksAndKeepsSource()
    {
        var (transfer, _) = await ArrangeApprovedAsync();
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Locked);

        var blocked = await RunUntilAsync(TestUsers.Admin, transfer.Id, IsBlockedOrDone);

        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.TargetLocked, blocked.BlockReason);
        Assert.Equal(SeededSecrets, (await ListAllSecretsAsync(TestUsers.User)).Count);
    }

    [Fact]
    public async Task Job_TargetUnlocked_ResumesAndCompletes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseFakeClock(clock);
        var (transfer, _) = await ArrangeApprovedAsync();
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Locked);
        await RunUntilAsync(TestUsers.Admin, transfer.Id, IsBlockedOrDone);
        clock.Advance(TimeSpan.FromMinutes(2));
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Active);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(SeededSecrets, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Job_SourceUnlockedBeforeGate_BlocksAndKeepsSource()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecrets, folderCount: 0);
        await SetUserStatusAsync(TestUsers.User.ObjectId, UserStatus.Locked);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, ReasonCategoryDto.Dismissed);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await SetUserStatusAsync(TestUsers.User.ObjectId, UserStatus.Active);

        var blocked = await RunUntilAsync(TestUsers.Admin, transfer.Id, IsBlockedOrDone);

        Assert.Equal(TransferStateDto.Blocked, blocked.State);
        Assert.Equal(TransferBlockReasonDto.SourceNotLocked, blocked.BlockReason);
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task Abort_WhileBlocked_RemovesStagingAndSourceIsIntact()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var stageCrash = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, afterEffect: true);
        UseHost(CrashingTransferHost(stageCrash, clock));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecrets, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        stageCrash.Arm();
        await Runner.RunOnceAsync(Token);
        Assert.True(stageCrash.Fired, "The runner never reached the staging crash point.");
        var staged = await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id);
        Assert.True(staged > 0, "The runner must have staged rows before the abort.");
        clock.Advance(TimeSpan.FromHours(1));
        await SetUserStatusAsync(TestUsers.OtherUser.ObjectId, UserStatus.Locked);
        var blocked = await RunUntilAsync(TestUsers.Admin, transfer.Id, IsBlockedOrDone);
        Assert.Equal(TransferStateDto.Blocked, blocked.State);

        var (response, body) = await ApiAs(TestUsers.Admin).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)),
            new TransferDecisionRequestDto { Reason = "Aborted for this test before any data moved" },
            ifMatch: blocked.ConcurrencyToken);
        Assert.True(response.IsSuccessStatusCode, $"Abort failed with {response.StatusCode}.");
        Assert.Equal(TransferStateDto.Aborted, body?.State);

        clock.Advance(TimeSpan.FromHours(1));
        await Runner.RunOnceAsync(Token);

        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
        Assert.Equal(seeded.Secrets.Count, (await ListAllSecretsAsync(TestUsers.User)).Count);
        Assert.Equal(0, await SecretRowCountAsync(TestUsers.OtherUser.ObjectId));
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.TransferAborted));
    }

    [Fact]
    public async Task Abort_ByTargetAdmin_Returns404()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.ThirdAdmin, TestUsers.User);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.ThirdAdmin);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var (response, _) = await ApiAs(TestUsers.ThirdAdmin).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)),
            new TransferDecisionRequestDto { Reason = "Aborted by the target for this test" },
            ifMatch: transfer.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Transfer_Completed_NoticesGoToTargetRequesterAndApprover()
    {
        var (transfer, _) = await ArrangeApprovedAsync();
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        foreach (var recipient in new[] { TestUsers.OtherUser, TestUsers.Admin, TestUsers.SecondAdmin })
        {
            var notices = await ListNoticesAsync(recipient);
            Assert.Single(notices, notice => notice.TransferId == transfer.Id);
        }
    }

    [Fact]
    public async Task Transfer_Completed_NoticeNeverCarriesCategoryOrReason()
    {
        var (transfer, _) = await ArrangeApprovedAsync();
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var notice = (await ListNoticesAsync(TestUsers.OtherUser)).Single(candidate => candidate.TransferId == transfer.Id);
        var json = System.Text.Json.JsonSerializer.Serialize(notice);

        Assert.DoesNotContain(ValidReason, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Category", json, StringComparison.Ordinal);
        Assert.Contains(TestUsers.User.DisplayName, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_Success_WritesNoAuditUntilTheJobRuns()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await SeedVaultAsync(TestUsers.User, SeededSecrets, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.TransferApproved));
    }

    private async Task<(TransferDto Transfer, SeededVault Seeded)> ArrangeApprovedAsync()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, SeededSecrets, folderCount: 2);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var approved = await ApproveAsync(TestUsers.SecondAdmin, transfer);
        return (approved, seeded);
    }

    private async Task<IReadOnlyList<TransferNoticeDto>> ListNoticesAsync(TestUser viewer)
    {
        var (response, body) = await ApiAs(viewer).SendAsync<TransferNoticeListDto>(HttpMethod.Get, ApiRoutes.TransferNotices);
        Assert.True(response.IsSuccessStatusCode, $"Notices failed with {response.StatusCode}.");
        return body?.Items ?? [];
    }
}
