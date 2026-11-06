// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Vaults;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Transfers;
using SharpVault.IntegrationTests.ApiTests.Transfers.Crash;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;
using Xunit;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferCollisionTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string XValue = "x-source";

    private const string YValue = "y-source";

    private const int StagedPageSize = 100;

    private const int MaxRunsPerTransfer = 20;

    private static readonly TimeSpan HourStep = TimeSpan.FromHours(1);

    [Fact]
    public async Task SecretAlreadyAtTarget_JobCompletes_BothCopiesReadable_TargetOwnCopyUnchanged_AndSharesMove()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var owner = ApiAs(TestUsers.User);
        var x = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "X", Value = XValue }));
        var y = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Y", Value = YValue }));
        var grant = await owner.SendAsync<ShareGrantDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.SecretGrants, ("ownerId", TestUsers.User.ObjectId), ("secretId", x.Id)),
            new CreateShareGrantRequestDto { RecipientId = TestUsers.Admin.ObjectId, Permission = SharePermissionDto.View });
        Assert.True(grant.Response.IsSuccessStatusCode, $"Share failed with {grant.Response.StatusCode}.");

        string? plantedETag = null;
        UseHost(CollisionHost(clock, async (targetId, transferId, cancellationToken) =>
        {
            plantedETag = await PlantTargetCopyAsync(targetId, transferId, x.Id, cancellationToken);
        }));

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var settled = await RunToCompletionAsync(transfer.Id, clock);

        Assert.NotNull(plantedETag);
        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(1, settled.RenamedItemCount);
        Assert.Equal(2, settled.PublishedItemCount);

        var renamedId = VaultIds.TransferredSecretIdFor(transfer.Id, x.Id);
        var ownerIds = (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id).ToHashSet();
        var targetIds = (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id).ToHashSet();
        Assert.Empty(ownerIds);
        Assert.Contains(x.Id, targetIds);
        Assert.Contains(renamedId, targetIds);
        Assert.Contains(y.Id, targetIds);

        var targetOwnCopy = await Host.Services.GetRequiredService<IVaultStore>().GetSecretAsync(TestUsers.OtherUser.ObjectId, x.Id, Token);
        Assert.Equal(plantedETag, targetOwnCopy?.ETag);
        Assert.Equal(XValue, await RevealAsync(TestUsers.OtherUser, x.Id, RevealFieldDto.Value));
        Assert.Equal(XValue, await RevealAsync(TestUsers.OtherUser, renamedId, RevealFieldDto.Value));

        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.SecretTransferredUnderNewId, renamedId));
        Assert.Equal(1, await CountAuditEventsAsync(AuditEventType.ShareReassigned, renamedId));
        Assert.Equal(0, await CountAuditEventsAsync(AuditEventType.ShareReassigned, x.Id));

        var recipient = ApiAs(TestUsers.Admin);
        var viaTarget = await recipient.SendAsync<SharedSecretDto>(
            HttpMethod.Get,
            VaultTestApi.Bind(ApiRoutes.SharedSecret, ("ownerId", TestUsers.OtherUser.ObjectId), ("secretId", renamedId)));
        Assert.Equal(HttpStatusCode.OK, viaTarget.Response.StatusCode);
        var viaSource = await recipient.SendAsync<SharedSecretDto>(
            HttpMethod.Get,
            VaultTestApi.Bind(ApiRoutes.SharedSecret, ("ownerId", TestUsers.User.ObjectId), ("secretId", x.Id)));
        Assert.Equal(HttpStatusCode.NotFound, viaSource.Response.StatusCode);
    }

    [Fact]
    public async Task OutOfSelectionSecretWithTargetCopy_GrantStaysWithSource()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var owner = ApiAs(TestUsers.User);
        var folder = await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "Selected" });
        Assert.True(folder.Response.IsSuccessStatusCode, $"Folder failed with {folder.Response.StatusCode}.");
        var folderId = folder.Body!.Id;
        var inside = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = "Inside",
            Value = YValue,
            FolderId = folderId,
        }));
        var outside = Ok(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "X", Value = XValue }));
        var grant = await owner.SendAsync<ShareGrantDto>(
            HttpMethod.Post,
            VaultTestApi.Bind(ApiRoutes.SecretGrants, ("ownerId", TestUsers.User.ObjectId), ("secretId", outside.Id)),
            new CreateShareGrantRequestDto { RecipientId = TestUsers.Admin.ObjectId, Permission = SharePermissionDto.View });
        Assert.True(grant.Response.IsSuccessStatusCode, $"Share failed with {grant.Response.StatusCode}.");
        Ok(await ApiAs(TestUsers.OtherUser).CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Own", Value = XValue }));
        await PlantSourceCopyAtTargetAsync(outside.Id);

        var transfer = await RequestAsync(
            TestUsers.Admin,
            TestUsers.User,
            TestUsers.OtherUser,
            ReasonCategoryDto.RoleChange,
            TransferScopeKindDto.SelectedFolders,
            [folderId]);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Contains(inside.Id, (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id));
        var recipient = ApiAs(TestUsers.Admin);
        var viaTarget = await recipient.SendAsync<SharedSecretDto>(
            HttpMethod.Get,
            VaultTestApi.Bind(ApiRoutes.SharedSecret, ("ownerId", TestUsers.OtherUser.ObjectId), ("secretId", outside.Id)));
        Assert.Equal(HttpStatusCode.NotFound, viaTarget.Response.StatusCode);
        var viaSource = await recipient.SendAsync<SharedSecretDto>(
            HttpMethod.Get,
            VaultTestApi.Bind(ApiRoutes.SharedSecret, ("ownerId", TestUsers.User.ObjectId), ("secretId", outside.Id)));
        Assert.Equal(HttpStatusCode.OK, viaSource.Response.StatusCode);
        Assert.Equal(0, await CountAuditEventsAsync(AuditEventType.ShareReassigned, outside.Id));
    }

    private async Task PlantSourceCopyAtTargetAsync(Guid secretId)
    {
        var vaults = Host.Services.GetRequiredService<IVaultStore>();
        var source = await vaults.GetSecretAsync(TestUsers.User.ObjectId, secretId, Token)
            ?? throw new InvalidOperationException("The source secret is missing.");
        var copy = source.Value;
        copy.OwnerId = TestUsers.OtherUser.ObjectId;
        var ring = await vaults.GetKeyRingAsync(TestUsers.OtherUser.ObjectId, Token)
            ?? throw new InvalidOperationException("The target ring is missing.");
        await vaults.SubmitAsync(TestUsers.OtherUser.ObjectId, [VaultChange.TouchKeyRing(ring.ETag), VaultChange.AddSecret(copy)], Token);
    }

    private async Task<string> PlantTargetCopyAsync(Guid targetId, Guid transferId, Guid secretId, CancellationToken cancellationToken)
    {
        var staging = Host.Services.GetRequiredService<ITransferStagingStore>();
        var vaults = Host.Services.GetRequiredService<IVaultStore>();
        var page = await staging.ReadSecretsAsync(targetId, transferId, StagedPageSize, null, cancellationToken);
        var staged = page.Items.Single(item => item.Secret.Id == secretId);
        var ring = await vaults.GetKeyRingAsync(targetId, cancellationToken) ?? throw new InvalidOperationException("The target ring is missing.");
        await vaults.SubmitAsync(targetId, [VaultChange.TouchKeyRing(ring.ETag), VaultChange.AddSecret(staged.Secret)], cancellationToken);
        var planted = await vaults.GetSecretAsync(targetId, secretId, cancellationToken) ?? throw new InvalidOperationException("The planted copy is missing.");
        return planted.ETag;
    }

    private async Task<TransferDto> RunToCompletionAsync(Guid transferId, FakeTimeProvider clock)
    {
        var settled = await GetTransferAsync(TestUsers.Admin, transferId);
        for (var run = 0; run < MaxRunsPerTransfer && settled.State != TransferStateDto.Completed; run++)
        {
            await Runner.RunOnceAsync(Token);
            clock.Advance(HourStep);
            settled = await GetTransferAsync(TestUsers.Admin, transferId);
        }

        return settled;
    }

    private static T Ok<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"The request failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The response had no body.");
    }

    private WebApplicationFactory<Program> CollisionHost(FakeTimeProvider clock, Func<Guid, Guid, CancellationToken, Task> beforeFirstSecretPublish)
    {
        var fired = 0;
        return Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            services.Replace(ServiceDescriptor.Singleton<ITransferStagingStore>(sp =>
                HookProxy.Wrap<ITransferStagingStore>(
                    ActivatorUtilities.CreateInstance<TableTransferStagingStore>(sp),
                    async (method, args) =>
                    {
                        if (method.Name == nameof(ITransferStagingStore.PublishAsync)
                            && args is [Guid targetId, Guid transferId, IReadOnlyList<PublishGroup> groups, CancellationToken cancellationToken]
                            && groups.Any(group => group.Secret is not null)
                            && Interlocked.Exchange(ref fired, 1) == 0)
                        {
                            await beforeFirstSecretPublish(targetId, transferId, cancellationToken);
                        }
                    })));
        }));
    }
}
