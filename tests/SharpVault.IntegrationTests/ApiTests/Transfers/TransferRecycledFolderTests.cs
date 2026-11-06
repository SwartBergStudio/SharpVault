// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferRecycledFolderTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task RecycledFolderOnBothSides_StaysRecycledAfterTheTransfer()
    {
        await WarmUpAllAsync(AllParticipants);
        var owner = ApiAs(TestUsers.User);
        var target = ApiAs(TestUsers.OtherUser);
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "Recycled on both sides" }));
        Require(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Inside", Value = "v", FolderId = folder.Id }));
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        await CopyRecycledFolderRowAsync(TestUsers.User, TestUsers.OtherUser, folder.Id);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        var targetFolders = Require(await target.ListFoldersAsync());
        Assert.DoesNotContain(targetFolders.Items, item => item.Id == folder.Id);
        var targetBin = Require(await target.ListRecycleBinAsync());
        Assert.Contains(targetBin.Items, item => item.Id == folder.Id && item.Kind == RecycleBinItemKindDto.Folder);
        var sourceBin = Require(await owner.ListRecycleBinAsync());
        Assert.DoesNotContain(sourceBin.Items, item => item.Id == folder.Id);
    }

    private async Task CopyRecycledFolderRowAsync(TestUser source, TestUser target, Guid folderId)
    {
        var stored = await VaultsTable.GetEntityAsync<TableEntity>(
            VaultKeys.PartitionKey(source.ObjectId),
            VaultKeys.FolderRowKey(folderId, recycled: true),
            cancellationToken: Token);
        var copy = new TableEntity(VaultKeys.PartitionKey(target.ObjectId), VaultKeys.FolderRowKey(folderId, recycled: true));
        foreach (var key in stored.Value.Keys.Where(key => key is not (nameof(TableEntity.PartitionKey) or nameof(TableEntity.RowKey) or nameof(TableEntity.Timestamp))))
        {
            copy[key] = stored.Value[key];
        }

        await VaultsTable.AddEntityAsync(copy, Token);
    }
}
