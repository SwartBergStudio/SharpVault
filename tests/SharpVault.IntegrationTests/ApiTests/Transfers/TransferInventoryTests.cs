// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferInventoryTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static readonly string[] AllowedInventoryColumns = ["Kind", "ItemId", "ContainerId", "CreatedAt", "ETag"];

    private async Task<VaultInventoryPage> ReadInventoryAsync(Guid owner, string? token = null)
    {
        using var scope = Host.Services.CreateScope();
        var vaults = scope.ServiceProvider.GetRequiredService<IVaultStore>();
        return await vaults.ListInventoryAsync(owner, token, Token);
    }

    [Fact]
    public async Task Inventory_ListsTheOwnersSecretsAndFoldersOnly()
    {
        await WarmUpAllAsync(TestUsers.User, TestUsers.OtherUser);
        var mine = await SeedVaultAsync(TestUsers.User, secretCount: 3, folderCount: 2);
        var theirs = await SeedVaultAsync(TestUsers.OtherUser, secretCount: 2, folderCount: 0);

        var page = await ReadInventoryAsync(TestUsers.User.ObjectId);

        var ids = page.Rows.Select(row => row.ItemId).ToHashSet();
        foreach (var secret in mine.Secrets)
        {
            Assert.Contains(secret.Id, ids);
        }

        foreach (var folder in mine.Folders)
        {
            Assert.Contains(folder.Id, ids);
        }

        foreach (var secret in theirs.Secrets)
        {
            Assert.DoesNotContain(secret.Id, ids);
        }
    }

    [Fact]
    public async Task Inventory_EveryQueryCarriesTheOwnerPartitionKey()
    {
        await WarmUpAllAsync(TestUsers.User);
        await SeedVaultAsync(TestUsers.User, secretCount: 2, folderCount: 1);
        Factory.StorageRequests.Clear();

        await ReadInventoryAsync(TestUsers.User.ObjectId);

        Assert.NotEmpty(Factory.StorageRequests.Requests);
        Assert.Empty(Factory.StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public void Inventory_RowType_CarriesNoSecretColumns()
    {
        var columns = typeof(VaultInventoryRow).GetProperties().Select(property => property.Name).Order().ToArray();

        Assert.Equal(AllowedInventoryColumns.Order().ToArray(), columns);
    }

    [Fact]
    public async Task Inventory_MalformedToken_ThrowsInvalidContinuationToken()
    {
        await WarmUpAllAsync(TestUsers.User);

        await Assert.ThrowsAsync<InvalidContinuationTokenException>(() => ReadInventoryAsync(TestUsers.User.ObjectId, "not-a-token"));
    }

    [Fact]
    public async Task Inventory_EmptyVault_ReturnsNoRows()
    {
        await WarmUpAllAsync(TestUsers.User);

        var page = await ReadInventoryAsync(TestUsers.User.ObjectId);

        Assert.Empty(page.Rows);
        Assert.Null(page.ContinuationToken);
    }
}
