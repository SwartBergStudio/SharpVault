// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Users;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Sharing;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferShareReassignTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string CrashPoint = "IShareInboxStore.AddIfAbsentAsync";


    private static TestUser[] AllUsers =>
    [
        TestUsers.Admin,
        TestUsers.SecondAdmin,
        TestUsers.User,
        TestUsers.OtherUser,
        TestUsers.ShareViewer,
    ];

    private SharingTestApi SharingFor(TestUser user) => new(ApiAs(user).Client);

    private async Task<Guid> CreateSecretAsync(TestUser owner, Guid? folderId = null)
    {
        var (response, body) = await ApiAs(owner).CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = $"Shared {Guid.NewGuid():N}",
            Value = $"share-value-{Guid.NewGuid():N}",
            FolderId = folderId,
        });
        Assert.True(response.IsSuccessStatusCode, $"Create failed with {response.StatusCode}.");
        return body!.Id;
    }

    private async Task GrantSecretAsync(TestUser owner, Guid secretId, TestUser recipient, SharePermissionDto permission)
    {
        var (response, _) = await SharingFor(owner).CreateSecretGrantAsync(
            owner.ObjectId,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = recipient.ObjectId, Permission = permission });
        Assert.True(response.IsSuccessStatusCode, $"Grant failed with {response.StatusCode}.");
    }

    private async Task<HttpStatusCode> ReadSharedAsync(TestUser viewer, TestUser owner, Guid secretId)
    {
        var (response, _) = await SharingFor(viewer).GetSharedSecretAsync(owner.ObjectId, secretId);
        return response.StatusCode;
    }

    private async Task<List<ShareGrantDto>> GrantsAsync(TestUser owner, Guid secretId)
    {
        var (response, body) = await SharingFor(owner).ListSecretGrantsAsync(owner.ObjectId, secretId);
        Assert.True(response.IsSuccessStatusCode, $"Grant list failed with {response.StatusCode}.");
        return body?.Items ?? [];
    }

    private async Task<HttpStatusCode> GrantStatusAsync(TestUser owner, Guid secretId)
    {
        var (response, _) = await SharingFor(owner).ListSecretGrantsAsync(owner.ObjectId, secretId);
        return response.StatusCode;
    }

    private async Task<TransferDto> RunTransferAsync(
        TestUser source,
        TestUser target,
        TransferScopeKindDto scope = TransferScopeKindDto.AllItems,
        List<Guid>? folderIds = null)
    {
        var transfer = await RequestAsync(TestUsers.Admin, source, target, ReasonCategoryDto.RoleChange, scope, folderIds);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        return settled;
    }

    [Fact]
    public async Task Transfer_ExistingShare_RecipientReadsThroughTheNewOwner()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        var value = await RevealAsync(TestUsers.User, secretId, RevealFieldDto.Value);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        Assert.Equal(HttpStatusCode.OK, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.User, secretId));

        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        Assert.Equal(HttpStatusCode.NotFound, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.User, secretId));
        Assert.Equal(HttpStatusCode.OK, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.OtherUser, secretId));
        var (response, revealed) = await SharingFor(TestUsers.ShareViewer).RevealSharedSecretAsync(
            TestUsers.OtherUser.ObjectId,
            secretId,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.True(response.IsSuccessStatusCode, $"Reveal failed with {response.StatusCode}.");
        Assert.Equal(value, revealed?.Value);
    }

    [Fact]
    public async Task Transfer_ExistingShare_KeepsItsPermissionAndMovesTheGrant()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.Edit);

        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        Assert.Equal(HttpStatusCode.NotFound, await GrantStatusAsync(TestUsers.User, secretId));
        var moved = Assert.Single(await GrantsAsync(TestUsers.OtherUser, secretId));
        Assert.Equal(TestUsers.ShareViewer.ObjectId, moved.RecipientId);
        Assert.Equal(SharePermissionDto.Edit, moved.Permission);
    }

    [Fact]
    public async Task Transfer_ShareToTarget_IsRevokedAndTargetIsOwner()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.OtherUser, SharePermissionDto.View);

        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        Assert.DoesNotContain(await GrantsAsync(TestUsers.OtherUser, secretId), grant => grant.RecipientId == TestUsers.OtherUser.ObjectId);
        Assert.Contains(await ListAllSecretsAsync(TestUsers.OtherUser), secret => secret.Id == secretId);
        var (response, shared) = await SharingFor(TestUsers.OtherUser).ListSharedItemsAsync();
        Assert.True(response.IsSuccessStatusCode, $"Shared list failed with {response.StatusCode}.");
        Assert.DoesNotContain(shared?.Items ?? [], item => item.ItemId == secretId);
    }

    [Fact]
    public async Task Target_AfterTransfer_ListsAndRevokesInheritedShare()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        var inherited = Assert.Single(await GrantsAsync(TestUsers.OtherUser, secretId));
        Assert.Equal(TestUsers.ShareViewer.ObjectId, inherited.RecipientId);
        var revoke = await SharingFor(TestUsers.OtherUser).RevokeSecretGrantAsync(TestUsers.OtherUser.ObjectId, secretId, TestUsers.ShareViewer.ObjectId);

        Assert.True(revoke.IsSuccessStatusCode, $"Revoke failed with {revoke.StatusCode}.");
        Assert.Equal(HttpStatusCode.NotFound, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.OtherUser, secretId));
    }

    [Fact]
    public async Task Source_AfterTransfer_CannotRevokeOrAddShares()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        var revoke = await SharingFor(TestUsers.User).RevokeSecretGrantAsync(TestUsers.User.ObjectId, secretId, TestUsers.ShareViewer.ObjectId);
        var (add, _) = await SharingFor(TestUsers.User).CreateSecretGrantAsync(
            TestUsers.User.ObjectId,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.ShareEditor.ObjectId, Permission = SharePermissionDto.View });

        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, add.StatusCode);
        Assert.Single(await GrantsAsync(TestUsers.OtherUser, secretId));
    }

    [Fact]
    public async Task SelectedScope_OutOfScopeShares_StayWithSource()
    {
        await WarmUpAllAsync(AllUsers);
        var folder = await ApiAs(TestUsers.User).CreateFolderAsync(new CreateFolderRequestDto { Name = "Scoped" });
        var folderId = folder.Body!.Id;
        var inside = await CreateSecretAsync(TestUsers.User, folderId);
        var outside = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, outside, TestUsers.ShareViewer, SharePermissionDto.View);

        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser, TransferScopeKindDto.SelectedFolders, [folderId]);

        Assert.Contains(await ListAllSecretsAsync(TestUsers.OtherUser), secret => secret.Id == inside);
        Assert.Equal(HttpStatusCode.OK, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.User, outside));
        Assert.Single(await GrantsAsync(TestUsers.User, outside));
        Assert.Equal(HttpStatusCode.NotFound, await GrantStatusAsync(TestUsers.OtherUser, outside));
    }

    [Fact]
    public async Task Transfer_ShareToLockedRecipient_StaysSuspendedUntilUnlocked()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        await SetUserStatusAsync(TestUsers.ShareViewer.ObjectId, UserStatus.Locked);

        await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        Assert.Contains(await GrantsAsync(TestUsers.OtherUser, secretId), grant => grant.RecipientId == TestUsers.ShareViewer.ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.OtherUser, secretId));
        await SetUserStatusAsync(TestUsers.ShareViewer.ObjectId, UserStatus.Active);
        Assert.Equal(HttpStatusCode.OK, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.OtherUser, secretId));
    }

    [Fact]
    public async Task Reassign_RunTwice_IsIdempotent()
    {
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        var settled = await RunTransferAsync(TestUsers.User, TestUsers.OtherUser);

        await Runner.RunOnceAsync(Token);
        await Runner.RunOnceAsync(Token);

        Assert.Equal(TransferStateDto.Completed, (await GetTransferAsync(TestUsers.Admin, settled.Id)).State);
        Assert.Single(await GrantsAsync(TestUsers.OtherUser, secretId));
        Assert.Equal(HttpStatusCode.NotFound, await GrantStatusAsync(TestUsers.User, secretId));
    }

    [Fact]
    public async Task Reassign_CrashBetweenAddAndRevoke_FinishesOnNextRun()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        var plan = new CrashPlan(CrashPoint, 1, afterEffect: true);
        UseHost(CrashingShareHost(plan, clock));
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

        Assert.True(plan.Fired, "The crash point was never reached, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Completed, current.State);
        Assert.Single(await GrantsAsync(TestUsers.OtherUser, secretId));
        Assert.Equal(HttpStatusCode.NotFound, await GrantStatusAsync(TestUsers.User, secretId));
        Assert.Equal(HttpStatusCode.OK, await ReadSharedAsync(TestUsers.ShareViewer, TestUsers.OtherUser, secretId));
    }

    [Fact]
    public async Task Completed_AfterAFailedStep_IsNotReportedAsRetrying()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await WarmUpAllAsync(AllUsers);
        var secretId = await CreateSecretAsync(TestUsers.User);
        await GrantSecretAsync(TestUsers.User, secretId, TestUsers.ShareViewer, SharePermissionDto.View);
        var plan = new CrashPlan(CrashPoint, 1, afterEffect: false);
        UseHost(CrashingShareHost(plan, clock));
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        plan.Arm();

        var current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
        var sawRetry = false;
        for (var run = 0; run < RunBound && current.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(TimeSpan.FromHours(1));
            current = await GetTransferAsync(TestUsers.Admin, transfer.Id);
            sawRetry |= current.IsRetrying;
        }

        Assert.True(plan.Fired, "The crash point was never reached, so the case proves nothing.");
        Assert.True(sawRetry, "The failed step was never reported as retrying, so the case proves nothing.");
        Assert.Equal(TransferStateDto.Completed, current.State);
        Assert.False(current.IsRetrying);
        Assert.Null(current.FailingSince);
    }

    private WebApplicationFactory<Program> CrashingShareHost(CrashPlan plan, FakeTimeProvider clock) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<IShareInboxStore>(sp =>
                CrashProxies.Wrap<IShareInboxStore>(ActivatorUtilities.CreateInstance<TableShareInboxStore>(sp), plan)));
        }));
}
