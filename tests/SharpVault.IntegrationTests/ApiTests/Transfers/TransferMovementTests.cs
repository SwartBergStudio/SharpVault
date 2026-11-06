// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Cryptography;
using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferMovementTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string RootNamePrefix = "Transferred from ";

    [Fact]
    public async Task Transfer_HistoryVersions_RevealForTargetInTheSameOrder()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "History login", "value-one");
        secret = await UpdateValueAsync(TestUsers.User, secret, "value-two");
        secret = await UpdateValueAsync(TestUsers.User, secret, "value-three");
        var before = await HistoryRevealsAsync(TestUsers.User, secret.Id);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var after = await HistoryRevealsAsync(TestUsers.OtherUser, secret.Id);
        Assert.Equal(["value-two", "value-one"], before.Select(version => version.Value));
        Assert.Equal(before, after);
        Assert.Equal("value-three", await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
    }

    [Fact]
    public async Task Transfer_SecretValueAndNotes_DecryptForTargetWithValueIntact()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Intact login", "intact-value-7731", notes: "intact-notes-7731");
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal("intact-value-7731", await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Value));
        Assert.Equal("intact-notes-7731", await RevealAsync(TestUsers.OtherUser, secret.Id, RevealFieldDto.Notes));
    }

    [Fact]
    public async Task Transfer_RecycledItems_KeepTheirPurgeDate()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Binned login", "binned-value");
        await ApiAs(TestUsers.User).DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);
        var sourceItem = await BinItemAsync(TestUsers.User, secret.Id);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var targetItem = await BinItemAsync(TestUsers.OtherUser, secret.Id);
        Assert.Equal(sourceItem.DeletedAt, targetItem.DeletedAt);
        Assert.Equal(sourceItem.PurgeAfter, targetItem.PurgeAfter);
        Assert.Equal(sourceItem.DeletionBatchId, targetItem.DeletionBatchId);
    }

    [Fact]
    public async Task Transfer_NestedFolders_KeepStructureUnderTheTransferredFolder()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var parent = await CreateFolderAsync(TestUsers.User, "Parent folder", null);
        var child = await CreateFolderAsync(TestUsers.User, "Child folder", parent.Id);
        var secret = await CreateSecretAsync(TestUsers.User, "Nested login", "nested-value", folderId: child.Id);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var folders = await ListFoldersAsync(TestUsers.OtherUser);
        var root = Assert.Single(folders, folder => folder.ParentId is null);
        Assert.StartsWith(RootNamePrefix, root.Name, StringComparison.Ordinal);
        Assert.Equal(root.Id, folders.Single(folder => folder.Id == parent.Id).ParentId);
        Assert.Equal(parent.Id, folders.Single(folder => folder.Id == child.Id).ParentId);
        var moved = await ListAllSecretsAsync(TestUsers.OtherUser);
        Assert.Equal(child.Id, moved.Single(item => item.Id == secret.Id).FolderId);
    }

    [Fact]
    public async Task Transfer_RootFolderName_HasSourceNameAndDate()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateSecretAsync(TestUsers.User, "Named login", "named-value");
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        var approved = await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var root = (await ListFoldersAsync(TestUsers.OtherUser)).Single(folder => folder.ParentId is null);
        var decidedOn = approved.DecidedAt!.Value.UtcDateTime.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal($"{RootNamePrefix}{TestUsers.User.DisplayName} ({decidedOn})", root.Name);
    }

    [Fact]
    public async Task Transfer_TargetWithExistingVault_KeepsTheirItems()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        await CreateSecretAsync(TestUsers.OtherUser, "Own login one", "own-one");
        await CreateSecretAsync(TestUsers.OtherUser, "Own login two", "own-two");
        await SeedVaultAsync(TestUsers.User, secretCount: 3, folderCount: 0);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(5, (await ListAllSecretsAsync(TestUsers.OtherUser)).Count);
    }

    [Fact]
    public async Task Transfer_SourceHasNoVault_CompletesWithNoRootFolder()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Empty(await ListFoldersAsync(TestUsers.OtherUser));
    }

    [Fact]
    public async Task Transfer_SelectedFolders_LeavesOtherItemsWithSource()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var kept = await CreateFolderAsync(TestUsers.User, "Moving folder", null);
        var stays = await CreateFolderAsync(TestUsers.User, "Staying folder", null);
        var movingSecret = await CreateSecretAsync(TestUsers.User, "Moving login", "moving-value", folderId: kept.Id);
        var stayingSecret = await CreateSecretAsync(TestUsers.User, "Staying login", "staying-value", folderId: stays.Id);
        var unfiled = await CreateSecretAsync(TestUsers.User, "Unfiled login", "unfiled-value");

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser, scope: TransferScopeKindDto.SelectedFolders, folderIds: [kept.Id]);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal([movingSecret.Id], (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id));
        Assert.Equal(new HashSet<Guid> { stayingSecret.Id, unfiled.Id }, (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id).ToHashSet());
    }

    [Fact]
    public async Task Job_DamagedSecret_IsSkippedStaysWithSourceAndIsReported()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var healthy = await CreateSecretAsync(TestUsers.User, "Healthy login", "healthy-value");
        var damaged = await CreateSecretAsync(TestUsers.User, "Damaged login", "damaged-value");
        await DamageValueAsync(TestUsers.User.ObjectId, damaged.Id);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        Assert.Equal(1, settled.SkippedItemCount);
        Assert.Equal([healthy.Id], (await ListAllSecretsAsync(TestUsers.OtherUser)).Select(item => item.Id));
        Assert.Contains(damaged.Id, (await ListAllSecretsAsync(TestUsers.User)).Select(item => item.Id));
        Assert.Single(await VaultAuditRowsAsync(AuditEventType.TransferItemSkipped));
    }

    [Fact]
    public async Task Source_AfterCompletion_GetsNotFoundForMovedItems()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Moved login", "moved-value");
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        var (get, _) = await ApiAs(TestUsers.User).GetSecretAsync(secret.Id);
        var reveal = await ApiAs(TestUsers.User).RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, reveal.Response.StatusCode);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }

    [Fact]
    public async Task Approver_AfterCompletion_GetsNotFoundForTransferredItems()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var secret = await CreateSecretAsync(TestUsers.User, "Hidden login", "hidden-value");
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        foreach (var party in new[] { TestUsers.Admin, TestUsers.SecondAdmin })
        {
            var (response, _) = await ApiAs(party).GetSecretAsync(secret.Id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    private async Task<SecretDto> CreateSecretAsync(TestUser owner, string name, string value, string? notes = null, Guid? folderId = null)
    {
        var body = new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = name, Value = value, Notes = notes, FolderId = folderId };
        var (response, secret) = await ApiAs(owner).CreateSecretAsync(body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return secret ?? throw new InvalidOperationException("The create response had no body.");
    }

    private async Task<SecretDto> UpdateValueAsync(TestUser owner, SecretDto current, string value)
    {
        var body = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = current.Name ?? string.Empty, ReplaceValue = true, Value = value };
        var (response, updated) = await ApiAs(owner).UpdateSecretAsync(current.Id, current.ConcurrencyToken, body);
        Assert.True(response.IsSuccessStatusCode, $"Update failed with {response.StatusCode}.");
        return updated ?? throw new InvalidOperationException("The update response had no body.");
    }

    private async Task<FolderDto> CreateFolderAsync(TestUser owner, string name, Guid? parentId)
    {
        var (response, folder) = await ApiAs(owner).CreateFolderAsync(new CreateFolderRequestDto { Name = name, ParentId = parentId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return folder ?? throw new InvalidOperationException("The folder response had no body.");
    }

    private async Task<IReadOnlyList<FolderDto>> ListFoldersAsync(TestUser owner)
    {
        var (response, body) = await ApiAs(owner).ListFoldersAsync();
        Assert.True(response.IsSuccessStatusCode, $"Folder list failed with {response.StatusCode}.");
        return body?.Items ?? [];
    }

    private async Task<RecycleBinItemDto> BinItemAsync(TestUser owner, Guid secretId)
    {
        var (response, page) = await ApiAs(owner).ListRecycleBinAsync(pageSize: 100);
        Assert.True(response.IsSuccessStatusCode, $"Bin list failed with {response.StatusCode}.");
        return Assert.Single(page?.Items ?? [], item => item.Id == secretId);
    }

    private async Task<IReadOnlyList<(Guid VersionId, string Value)>> HistoryRevealsAsync(TestUser owner, Guid secretId)
    {
        var (response, history) = await ApiAs(owner).ListHistoryAsync(secretId);
        Assert.True(response.IsSuccessStatusCode, $"History failed with {response.StatusCode}.");
        var reveals = new List<(Guid, string)>();
        foreach (var version in history?.Items ?? [])
        {
            var (revealResponse, body) = await ApiAs(owner).RevealVersionAsync(secretId, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View });
            Assert.True(revealResponse.IsSuccessStatusCode, $"Version reveal failed with {revealResponse.StatusCode}.");
            reveals.Add((version.VersionId, body?.Value ?? string.Empty));
        }

        return reveals;
    }

    private async Task DamageValueAsync(Guid owner, Guid secretId)
    {
        var entity = await VaultsTable.GetEntityAsync<TableEntity>(VaultKeys.PartitionKey(owner), VaultKeys.SecretRowKey(secretId, recycled: false), cancellationToken: Token);
        entity.Value["ValueEnvelope"] = RandomNumberGenerator.GetBytes(96);
        await VaultsTable.UpdateEntityAsync(entity.Value, entity.Value.ETag, TableUpdateMode.Replace, Token);
    }
}
