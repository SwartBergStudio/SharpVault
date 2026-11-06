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
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class FolderDeleteTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const string TouchedColumn = "Touched";
    private const int ThreeByteCharacterCount = VaultLimits.ValueMaxLength;
    private const string ThreeByteCharacter = "€";

    private int BatchRequestCount() =>
        StorageRequests.Requests.Count(request => request.Method == "POST" && request.Path.EndsWith("$batch", StringComparison.Ordinal));

    [Fact]
    public async Task DeleteFolder_MoveToParent_SecretsAndChildrenMoveUp()
    {
        var parent = await NewFolderAsync("Parent");
        var folder = await NewFolderAsync("Doomed", parent.Id);
        var child = await NewFolderAsync("Child", folder.Id);
        var inside = await NewSecretAsync(folder.Id);
        var inChild = await NewSecretAsync(child.Id);

        var response = await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var folders = Require(await Api.ListFoldersAsync()).Items;
        Assert.DoesNotContain(folders, item => item.Id == folder.Id);
        Assert.Equal(parent.Id, folders.Single(item => item.Id == child.Id).ParentId);
        Assert.Equal(parent.Id, Require(await Api.GetSecretAsync(inside.Id)).FolderId);
        Assert.Equal(child.Id, Require(await Api.GetSecretAsync(inChild.Id)).FolderId);
        var binned = Assert.Single(await WholeBinAsync());
        Assert.Equal(RecycleBinItemKindDto.Folder, binned.Kind);
        Assert.Equal(folder.Id, binned.Id);
    }

    [Fact]
    public async Task DeleteFolder_MoveToParentAtRoot_ContentsToRoot()
    {
        var folder = await NewFolderAsync("Doomed");
        var child = await NewFolderAsync("Child", folder.Id);
        var inside = await NewSecretAsync(folder.Id);

        var response = await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(Require(await Api.ListFoldersAsync()).Items.Single(item => item.Id == child.Id).ParentId);
        Assert.Null(Require(await Api.GetSecretAsync(inside.Id)).FolderId);
        Assert.Equal(inside.Id, Assert.Single(await WholeListAsync(unfiled: true)).Id);
    }

    [Fact]
    public async Task DeleteFolder_DeleteContents_SubtreeAndSecretsInBinWithOneBatch()
    {
        var folder = await NewFolderAsync("Doomed");
        var child = await NewFolderAsync("Child", folder.Id);
        var grandchild = await NewFolderAsync("Grandchild", child.Id);
        var first = await NewSecretAsync(folder.Id);
        var second = await NewSecretAsync(grandchild.Id);
        var survivor = await NewSecretAsync();
        var sibling = await NewFolderAsync("Sibling");

        var response = await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var bin = await WholeBinAsync();
        Assert.Equal(
            new[] { folder.Id, child.Id, grandchild.Id, first.Id, second.Id }.Order(),
            bin.Select(item => item.Id).Order());
        Assert.Equal(3, bin.Count(item => item.Kind == RecycleBinItemKindDto.Folder));
        Assert.Equal(2, bin.Count(item => item.Kind == RecycleBinItemKindDto.Secret));
        Assert.Single(bin.Select(item => item.DeletionBatchId).Distinct());
        Assert.Equal(VaultIds.DeletionBatchIdFor(folder.Id), bin[0].DeletionBatchId);
        Assert.Single(bin.Select(item => item.DeletedAt).Distinct());
        Assert.Equal(sibling.Id, Assert.Single(Require(await Api.ListFoldersAsync()).Items).Id);
        Assert.Equal(survivor.Id, Assert.Single(await WholeListAsync()).Id);
    }

    [Fact]
    public async Task DeleteFolder_MissingMode_Returns400AndNothingChanges()
    {
        var folder = await NewFolderAsync("Keep");
        var secret = await NewSecretAsync(folder.Id);

        var response = await Api.DeleteFolderAsync(folder.Id, (string?)null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(folder.Id, Assert.Single(Require(await Api.ListFoldersAsync()).Items).Id);
        Assert.Equal(folder.Id, Require(await Api.GetSecretAsync(secret.Id)).FolderId);
        Assert.Empty(await WholeBinAsync());
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("movetoparent")]
    [InlineData("deletecontents")]
    [InlineData("MOVETOPARENT")]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("")]
    [InlineData("MoveToParent,DeleteContents")]
    public async Task DeleteFolder_UnknownMode_Returns400(string mode)
    {
        var folder = await NewFolderAsync("Keep");
        var secret = await NewSecretAsync(folder.Id);

        var response = await Api.DeleteFolderAsync(folder.Id, mode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(folder.Id, Assert.Single(Require(await Api.ListFoldersAsync()).Items).Id);
        Assert.Equal(secret.Id, Assert.Single(await WholeListAsync()).Id);
        Assert.Empty(await WholeBinAsync());
    }

    [Fact]
    public async Task DeleteFolder_FiftyMaxSizeSecrets_MovesAllToBin()
    {
        var folder = await NewFolderAsync("Heavy");
        var heavy = string.Concat(Enumerable.Repeat(ThreeByteCharacter, ThreeByteCharacterCount));
        var ids = new List<Guid>();
        for (var index = 0; index < 50; index++)
        {
            var created = Require(await Api.CreateSecretAsync(new CreateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = $"Heavy {index}",
                Value = heavy,
                Notes = heavy,
                FolderId = folder.Id,
            }));
            ids.Add(created.Id);
        }

        StorageRequests.Clear();
        var response = await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(BatchRequestCount() >= 2);
        var bin = await WholeBinAsync();
        Assert.Equal(51, bin.Count);
        Assert.Equal(ids.Order(), bin.Where(item => item.Kind == RecycleBinItemKindDto.Secret).Select(item => item.Id).Order());
        Assert.Empty(await WholeListAsync());
        Assert.Empty(Require(await Api.ListFoldersAsync()).Items);
    }

    [Fact]
    public async Task DeleteFolder_250Secrets_SpansThreeTransactions()
    {
        var folder = await NewFolderAsync("Large");
        var seeded = await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 250, 0, new SeedPlacement(null, folder.Id), Token);

        StorageRequests.Clear();
        var response = await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(BatchRequestCount() >= 3);
        var bin = await WholeBinAsync();
        Assert.Equal(251, bin.Count);
        Assert.Equal(250, bin.Count(item => item.Kind == RecycleBinItemKindDto.Secret));
        Assert.Equal(seeded.Secrets.Select(secret => secret.Id).Order(), bin.Where(item => item.Kind == RecycleBinItemKindDto.Secret).Select(item => item.Id).Order());
        Assert.Single(bin.Select(item => item.DeletionBatchId).Distinct());
    }

    [Fact]
    public async Task DeleteFolder_SecondChunkConflicts_Returns409AndRetryCompletes()
    {
        var folder = await NewFolderAsync("Parent");
        var child = await NewFolderAsync("Child", folder.Id);
        await VaultSeeder.SeedAsync(Factory, Caller.ObjectId, 60, 0, new SeedPlacement(null, child.Id), Token);
        var racing = new RacingVaultStore(new TableVaultStore(new TableServiceClient(Factory.AzuriteConnectionString), NullLogger<TableVaultStore>.Instance));
        using var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVaultStore>();
            services.AddSingleton<IVaultStore>(racing);
        }));
        racing.AfterNextFolderSecretsRead((secrets, _) => VaultRows.SetPropertyAsync(
            VaultsTable,
            Caller.ObjectId,
            VaultKeys.SecretRowKey(secrets.MaxBy(secret => secret.Value.Id)!.Value.Id, recycled: false),
            TouchedColumn,
            1,
            Token));
        var racingApi = new VaultTestApi(CreateClientFor(host, Authentication.TestTokens.Create(Caller)));

        var first = await racingApi.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        var second = await racingApi.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var bin = await WholeBinAsync();
        Assert.Equal(62, bin.Count);
        Assert.Single(bin.Select(item => item.DeletionBatchId).Distinct());
        Assert.Equal(VaultIds.DeletionBatchIdFor(folder.Id), bin[0].DeletionBatchId);
        Assert.Empty(await WholeListAsync());
        Assert.Empty(Require(await Api.ListFoldersAsync()).Items);
    }
}
