// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
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
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
public sealed class TransferReviewRegressionTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int LargeSecretCharacters = 8192;
    private const int TransactionLimitProbeCount = 100;
    private const int SmallerProbeCount = 40;
    private const int FolderCountForOneRun = 25;
    private const int CharacterCjkIdeograph = 0x4E00;
    private const int CharacterLatinA = 'a';

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    private WebApplicationFactory<Program> Rig(FakeTimeProvider clock, HookedLeaseStore? hook, CrashPlan[] stagingPlans, CrashPlan[] vaultPlans) =>
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

                return store;
            }));
            services.Replace(ServiceDescriptor.Singleton<IVaultStore>(sp =>
            {
                IVaultStore store = ActivatorUtilities.CreateInstance<TableVaultStore>(sp);
                foreach (var plan in vaultPlans)
                {
                    store = CrashProxies.Wrap(store, plan);
                }

                return store;
            }));
            if (hook is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IMaintenanceLeaseStore>(sp =>
                {
                    hook.Inner = ActivatorUtilities.CreateInstance<TableMaintenanceLeaseStore>(sp);
                    return hook;
                }));
            }
        }));

    [Fact]
    public async Task StaleRunnerSnapshot_AfterAnotherRunnerTakesOver_DoesNotDestroyStagedCopies()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var hook = new HookedLeaseStore();
        var stageCrash = new CrashPlan("ITransferStagingStore.StageSecretsAsync", 1, false);
        var publishCrash = new CrashPlan("ITransferStagingStore.PublishAsync", 1, false);
        UseHost(Rig(clock, hook, [stageCrash, publishCrash], []));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, 3, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        stageCrash.Arm();
        await Runner.RunOnceAsync(Token);
        Assert.Equal(TransferStateDto.Staging, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);
        clock.Advance(TimeSpan.FromHours(1));

        var takeover = ActivatorUtilities.CreateInstance<TransferJobRunner>(Host.Services);
        publishCrash.Arm();
        hook.BeforeNextAcquire = async () => await takeover.RunOnceAsync(Token);
        await Runner.RunOnceAsync(Token);

        var stagedAfterStaleRun = await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id);
        clock.Advance(TimeSpan.FromHours(1));
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
        var sourceLeft = (await ListAllSecretsAsync(TestUsers.User)).Count;
        var targetGot = (await ListAllSecretsAsync(TestUsers.OtherUser)).Count;

        Assert.True(publishCrash.Fired, "The publish crash never fired, so the stale runner was not exercised.");
        Assert.True(
            sourceLeft + targetGot == 3,
            $"Secrets were lost: stagedRowsAfterStaleRun={stagedAfterStaleRun} final={settled.State} sourceSecrets={sourceLeft} targetSecrets={targetGot}");
    }

    [Fact]
    public async Task TwentyFiveFolders_OneRun_CompletesTheTransfer()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseHost(Rig(clock, null, [], []));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, 1, FolderCountForOneRun);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        await Runner.RunOnceAsync(Token);
        var afterOneRun = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        var folders = Require(await ApiAs(TestUsers.User).ListFoldersAsync());

        Assert.True(
            afterOneRun.State == TransferStateDto.Completed,
            $"After one run: state={afterOneRun.State} isRetrying={afterOneRun.IsRetrying} sourceFoldersLeft={folders?.Items.Count}");
    }

    [Theory]
    [InlineData(CharacterCjkIdeograph, TransactionLimitProbeCount)]
    [InlineData(CharacterLatinA, TransactionLimitProbeCount)]
    [InlineData(CharacterCjkIdeograph, SmallerProbeCount)]
    public async Task LargeSecrets_VaryingCharacterAndCount_CompleteInOneRun(int character, int count)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        UseHost(Rig(clock, null, [], []));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        var text = new string((char)character, LargeSecretCharacters);
        for (var index = 0; index < count; index++)
        {
            var (response, _) = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = $"Big {index}", Value = text, Notes = text });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await Runner.RunOnceAsync(Token);
        var current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        var job = await Host.Services.GetRequiredService<ITransferJobStore>().GetAsync(transfer.Id, Token);

        Assert.True(
            current.State == TransferStateDto.Completed,
            $"character=U+{character:X4} count={count} state={current.State} failure={job?.Value.LastFailureKind} attempts={job?.Value.Attempts}");
    }

    [Fact]
    public async Task PendingTransfer_ApproverSeesItemAndFolderCounts()
    {
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, 3, 2);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);

        var seen = await GetTransferAsync(TestUsers.SecondAdmin, transfer.Id);

        Assert.True(
            seen.ItemCount == 3 && seen.FolderCount == 2,
            $"Approver sees ItemCount={seen.ItemCount} FolderCount={seen.FolderCount} for a vault of 3 secrets and 2 folders");
    }

    [Fact]
    public async Task NoticeItemCount_ExcludesDamagedItemsThatWereSkipped()
    {
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Healthy", Value = "v1" });
        var damaged = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Damaged", Value = "v2" }));
        var entity = await VaultsTable.GetEntityAsync<TableEntity>(VaultKeys.PartitionKey(TestUsers.User.ObjectId), VaultKeys.SecretRowKey(damaged.Id, recycled: false), cancellationToken: Token);
        entity.Value["ValueEnvelope"] = RandomNumberGenerator.GetBytes(96);
        await VaultsTable.UpdateEntityAsync(entity.Value, entity.Value.ETag, TableUpdateMode.Replace, Token);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
        var notices = Require(await ApiAs(TestUsers.OtherUser).SendAsync<TransferNoticeListDto>(HttpMethod.Get, ApiRoutes.TransferNotices));
        var notice = Assert.Single(notices.Items);
        var moved = (await ListAllSecretsAsync(TestUsers.OtherUser)).Count;

        Assert.True(
            notice.ItemCount == moved,
            $"Notice says {notice.ItemCount} items moved, target has {moved}, skipped={settled.SkippedItemCount}, published={settled.PublishedItemCount}");
    }

    [Fact]
    public async Task DismissedLock_RevokeFailsMidway_AuditsWhatWasRevokedAndRetryFinishes()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var submitCrash = new CrashPlan("IVaultStore.SubmitAsync", 2, false);
        UseHost(Rig(clock, null, [], [submitCrash]));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, 2, 0);
        var secrets = await ListAllSecretsAsync(TestUsers.User);
        var sharing = new SharingTestApi(ApiAs(TestUsers.User).Client);
        foreach (var secret in secrets)
        {
            var (grant, _) = await sharing.CreateSecretGrantAsync(TestUsers.User.ObjectId, secret.Id, new CreateShareGrantRequestDto { RecipientId = TestUsers.OtherUser.ObjectId, Permission = SharePermissionDto.View });
            Assert.True(grant.IsSuccessStatusCode, grant.StatusCode.ToString());
        }

        submitCrash.Arm();
        var json = JsonSerializer.Serialize(new { category = "Dismissed", reason = "Dismissed, shares end with the employment" });
        var path = VaultTestApi.Bind(ApiRoutes.AdminUserLock, ("userId", TestUsers.OtherUser.ObjectId));
        var first = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, path, json, null);
        var owned = Require(await ApiAs(TestUsers.User).SendAsync<OwnedSharePageDto>(HttpMethod.Get, ApiRoutes.TransferOwnedShares));
        var remaining = owned.Items.Count;
        var revokeAudits = 0;
        foreach (var secret in secrets)
        {
            revokeAudits += (await SecretAuditRowsAsync(TestUsers.User.ObjectId, secret.Id)).Count(row => row.GetString("EventType") == "SecretShareRevoked");
        }

        var retry = await ApiAs(TestUsers.Admin).SendRawAsync(HttpMethod.Post, path, json, null);
        var ownedAfter = Require(await ApiAs(TestUsers.User).SendAsync<OwnedSharePageDto>(HttpMethod.Get, ApiRoutes.TransferOwnedShares));

        Assert.True(submitCrash.Fired, "The revoke crash never fired.");
        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.Equal(1, remaining);
        Assert.Equal(1, revokeAudits);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Empty(ownedAfter.Items);
        Assert.Equal(2, await CountAuditEventsAsync(AuditEventType.UserLocked, TestUsers.OtherUser.ObjectId));
    }

    [Fact]
    public async Task AbortInStagingWithStagedRows_CleansStagingAndAllowsANewRequest()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var crash = new CrashPlan("ITransferStagingStore.StageFoldersAsync", 1, false);
        UseHost(Rig(clock, null, [crash], []));
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, 3, 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        crash.Arm();
        await Runner.RunOnceAsync(Token);
        var staging = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        Assert.Equal(TransferStateDto.Staging, staging.State);
        var stagedBeforeAbort = await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id);
        Assert.True(stagedBeforeAbort > 0, "The runner did not stage any rows before the abort.");

        var (abort, _) = await ApiAs(TestUsers.Admin).SendAsync<TransferDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.AdminTransferAbort, ("transferId", transfer.Id)),
            new TransferDecisionRequestDto { Reason = "Aborted for this review regression" },
            ifMatch: staging.ConcurrencyToken);
        Assert.True(abort.IsSuccessStatusCode, abort.StatusCode.ToString());
        clock.Advance(TimeSpan.FromHours(1));
        await Runner.RunOnceAsync(Token);

        Assert.Equal(0, await StagedRowCountAsync(TestUsers.OtherUser.ObjectId, transfer.Id));
        Assert.Equal(3, (await ListAllSecretsAsync(TestUsers.User)).Count);
        var again = await CreateRawAsync(TestUsers.Admin, CreateBody(TestUsers.User, TestUsers.OtherUser));
        Assert.Equal(HttpStatusCode.Created, again.Response.StatusCode);
    }

    [Fact]
    public async Task OwnerEditDuringRelease_TargetReceivesLatestValueAndOneHistoryVersion()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var crash = new CrashPlan("IVaultStore.SubmitAsync", 1, false);
        UseHost(Rig(clock, null, [], [crash]));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        var secret = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Editable", Value = "before-edit" }));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        crash.Arm();
        await Runner.RunOnceAsync(Token);
        Assert.Equal(TransferStateDto.Releasing, (await GetTransferAsync(TestUsers.Admin, transfer.Id)).State);

        var (update, _) = await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Editable", ReplaceValue = true, Value = "after-edit" });
        Assert.True(update.IsSuccessStatusCode, update.StatusCode.ToString());
        clock.Advance(TimeSpan.FromHours(1));
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal("after-edit", await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
        var history = Require(await ApiAs(TestUsers.OtherUser).ListHistoryAsync(secret.Id));
        Assert.Single(history.Items);
    }

    [Fact]
    public async Task MovesOutOfScopeBeforeTheFirstReleasePack_StaysWithTheOwner()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        Func<Task> moveOutOfScope = () => Task.CompletedTask;
        UseHost(RaceHost(clock, BeforeFirstReleasePack(() => moveOutOfScope()), null));
        await WarmUpAllAsync(AllParticipants);
        var api = ApiAs(TestUsers.User);
        var top = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Top" }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Child", ParentId = top.Id }));
        var staying = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "InChild", Value = "v", FolderId = child.Id }));
        var goes = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Goes", Value = "v", FolderId = top.Id }));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.SelectedFolders, folderIds: [top.Id]);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var folderMoved = false;
        moveOutOfScope = async () =>
        {
            var (moveFolder, _) = await api.MoveFolderAsync(child.Id, new MoveFolderRequestDto { ParentId = null });
            folderMoved = moveFolder.IsSuccessStatusCode;
        };

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.True(folderMoved, "The folder move did not succeed inside the release window.");
        Assert.Equal(TransferStateDto.Completed, settled.State);
        var ownerIds = (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id).ToHashSet();
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id).ToHashSet();
        Assert.Contains(staying.Id, ownerIds);
        Assert.DoesNotContain(staying.Id, targetIds);
        Assert.Contains(goes.Id, targetIds);
        Assert.DoesNotContain(goes.Id, ownerIds);
    }

    [Fact]
    public async Task TargetRecycledFolderWithTheSameId_IsReplacedByTheActiveFolder_AndItsRecycledSecretsStayRecycled()
    {
        await WarmUpAllAsync(AllParticipants);
        var owner = ApiAs(TestUsers.User);
        var target = ApiAs(TestUsers.OtherUser);
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "Collide" }));
        Require(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Inside", Value = "v", FolderId = folder.Id }));
        var keeper = Require(await target.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Target keeper", Value = "v" }));
        Assert.Equal(HttpStatusCode.NoContent, (await target.DeleteSecretAsync(keeper.Id, keeper.ConcurrencyToken)).StatusCode);
        await CopyFolderRowAsync(TestUsers.User, TestUsers.OtherUser, folder.Id, recycled: true);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.True(settled.State == TransferStateDto.Completed, $"state={settled.State} isRetrying={settled.IsRetrying}");
        var folders = Require(await target.ListFoldersAsync());
        Assert.Contains(folders.Items, item => item.Id == folder.Id);
        var bin = Require(await target.ListRecycleBinAsync());
        Assert.Contains(bin.Items, item => item.Id == keeper.Id && item.Kind == RecycleBinItemKindDto.Secret);
        Assert.DoesNotContain(bin.Items, item => item.Id == folder.Id);
    }

    [Fact]
    public async Task TargetAlreadyHoldsActiveFolderWithTheSameId_MergesTheItemsIntoIt_AndCompletes()
    {
        await WarmUpAllAsync(AllParticipants);
        var owner = ApiAs(TestUsers.User);
        var target = ApiAs(TestUsers.OtherUser);
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "Retained" }));
        var inside = Require(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Inside", Value = "v", FolderId = folder.Id }));
        await CopyFolderRowAsync(TestUsers.User, TestUsers.OtherUser, folder.Id, recycled: false);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.True(settled.State == TransferStateDto.Completed, $"state={settled.State} isRetrying={settled.IsRetrying} skipped={settled.SkippedItemCount}");
        var folders = Require(await target.ListFoldersAsync());
        Assert.Single(folders.Items, item => item.Id == folder.Id);
        var moved = Assert.Single(await ListAllSecretsAsync(TestUsers.OtherUser), item => item.Id == inside.Id);
        Assert.Equal(folder.Id, moved.FolderId);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    private async Task CopyFolderRowAsync(TestUser source, TestUser target, Guid folderId, bool recycled)
    {
        var active = await VaultsTable.GetEntityAsync<TableEntity>(
            VaultKeys.PartitionKey(source.ObjectId),
            VaultKeys.FolderRowKey(folderId, recycled: false),
            cancellationToken: Token);
        var copy = new TableEntity(VaultKeys.PartitionKey(target.ObjectId), VaultKeys.FolderRowKey(folderId, recycled));
        foreach (var property in active.Value.Keys.Where(key => key is not (nameof(TableEntity.PartitionKey) or nameof(TableEntity.RowKey) or nameof(TableEntity.Timestamp))))
        {
            copy[property] = active.Value[property];
        }

        await VaultsTable.AddEntityAsync(copy, Token);
    }
}
