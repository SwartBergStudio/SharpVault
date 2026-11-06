// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultMultiChunkTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const string TouchedColumn = "Touched";

    private static readonly TestUser Caller = TestUsers.User;

    private (WebApplicationFactory<Program> Host, RacingVaultStore Store) RacingHost()
    {
        var racing = new RacingVaultStore(new TableVaultStore(new TableServiceClient(Factory.AzuriteConnectionString), NullLogger<TableVaultStore>.Instance));
        var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVaultStore>();
            services.AddSingleton<IVaultStore>(racing);
        }));
        return (host, racing);
    }

    private Task StaleAsync(string rowKey) => VaultRows.SetPropertyAsync(VaultsTable, Caller.ObjectId, rowKey, TouchedColumn, 1, Token);

    private static VaultTestApi ApiOn(WebApplicationFactory<Program> host) => new(CreateClientFor(host, TestTokens.Create(Caller)));

    [Fact]
    public async Task RestoreFolder_SecondChunkConflicts_RepeatFinishesAndEverySecretReturns()
    {
        await WarmUpAsync(Caller);
        var api = ApiFor(Caller);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Parent" }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Child", ParentId = folder.Id }));
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 60, 0, new SeedPlacement(null, child.Id), Token);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        var (host, racing) = RacingHost();
        using var _ = host;
        racing.AfterNextBatchRead((batch, _) => StaleAsync(VaultKeys.SecretRowKey(batch.Secrets.MaxBy(secret => secret.Value.Id)!.Value.Id, recycled: true)));
        var racingApi = ApiOn(host);

        var first = await racingApi.RestoreRecycledFolderAsync(folder.Id);
        var folderListMidway = await api.ListFoldersAsync();
        var secretListMidway = await api.ListSecretsAsync(pageSize: 200);
        var second = await racingApi.RestoreRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.Conflict, first.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, folderListMidway.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secretListMidway.Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.Response.StatusCode);
        var secrets = Require(await api.ListSecretsAsync(folderId: child.Id, pageSize: 200));
        Assert.Equal(60, secrets.Items.Count);
        Assert.Equal(2, Require(await api.ListFoldersAsync()).Items.Count);
        Assert.Empty(Require(await api.ListRecycleBinAsync(pageSize: 200)).Items);
    }

    [Fact]
    public async Task PurgeFolder_SecondChunkConflicts_RepeatFinishesAndTheBinEmpties()
    {
        await WarmUpAsync(Caller);
        var api = ApiFor(Caller);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Parent" }));
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 0, 250, new SeedPlacement(folder.Id, null), Token);
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
        var (host, racing) = RacingHost();
        using var _ = host;
        racing.AfterNextBatchRead((batch, _) => StaleAsync(VaultKeys.FolderRowKey(batch.Folders[120].Value.Id, recycled: true)));
        var racingApi = ApiOn(host);

        var first = await racingApi.PurgeRecycledFolderAsync(folder.Id);
        var second = await racingApi.PurgeRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Empty(Require(await api.ListRecycleBinAsync(pageSize: 200)).Items);
    }

    private async Task<(FolderDto Folder, FolderDto Child, VaultTestApi Api)> ArrangeRetriedDeleteAsync()
    {
        await WarmUpAsync(Caller);
        var api = ApiFor(Caller);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Parent" }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Child", ParentId = folder.Id }));
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 60, 0, new SeedPlacement(null, child.Id), Token);
        var (host, racing) = RacingHost();
        using var _ = host;
        racing.AfterNextFolderSecretsRead((secrets, _) => StaleAsync(VaultKeys.SecretRowKey(secrets.MaxBy(secret => secret.Value.Id)!.Value.Id, recycled: false)));
        var racingApi = ApiOn(host);

        var first = await racingApi.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        var second = await racingApi.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        return (folder, child, api);
    }

    [Fact]
    public async Task DeleteFolder_RetriedAfterSecondChunkConflict_RestoreReachesAllSixtySecrets()
    {
        var (folder, child, api) = await ArrangeRetriedDeleteAsync();

        var restored = await api.RestoreRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.OK, restored.Response.StatusCode);
        Assert.Equal(60, Require(await api.ListSecretsAsync(folderId: child.Id, pageSize: 200)).Items.Count);
        Assert.Empty(Require(await api.ListRecycleBinAsync(pageSize: 200)).Items);
    }

    [Fact]
    public async Task DeleteFolder_RetriedAfterSecondChunkConflict_PurgeReachesAllSixtySecrets()
    {
        var (folder, _, api) = await ArrangeRetriedDeleteAsync();

        var purged = await api.PurgeRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.NoContent, purged.StatusCode);
        Assert.Empty(Require(await api.ListRecycleBinAsync(pageSize: 200)).Items);
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, Caller.ObjectId, Token);
        Assert.DoesNotContain(rows, row => row.RowKey.StartsWith(VaultKeys.RecycledSecretPrefix, StringComparison.Ordinal));
    }
}
