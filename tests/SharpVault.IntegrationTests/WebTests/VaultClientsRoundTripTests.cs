// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;
using SharpVault.UI.Shared.Services;

namespace SharpVault.IntegrationTests.WebTests;

[Collection(nameof(WebApiTestGroup))]
public sealed class VaultClientsRoundTripTests : IAsyncLifetime
{
    private const string FirstValue = "round-trip-first-value-7f3a";
    private const string SecondValue = "round-trip-second-value-91c2";
    private const string NotesText = "round-trip-notes-55d0";

    private readonly ApiTestFactory _api;
    private readonly WebTestFactory _web;

    public VaultClientsRoundTripTests(ApiTestFactory api, WebTestFactory web)
    {
        _api = api;
        _web = web;
        _web.UseApi(api);
    }

    public ValueTask InitializeAsync()
    {
        _api.Logs.Clear();
        _web.Logs.Clear();
        _api.StorageRequests.Clear();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _api.ResetStorageAsync(CancellationToken.None);
        GC.SuppressFinalize(this);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_EveryMethod_RoundTripThroughTheRealApi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);
        var folders = circuit.VaultFolders;
        var secrets = circuit.VaultSecrets;
        var history = circuit.SecretHistory;
        var bin = circuit.RecycleBin;

        var parent = Succeeded(await folders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Round trip" }, cancellationToken));
        var child = Succeeded(await folders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Child", ParentId = parent.Id }, cancellationToken));
        var listed = Succeeded(await folders.ListFoldersAsync(cancellationToken));
        Assert.Equal([parent.Id, child.Id], listed.Items.Select(folder => folder.Id).Order());
        var renamed = Succeeded(await folders.RenameFolderAsync(child.Id, new RenameFolderRequestDto { Name = "Renamed" }, cancellationToken));
        Assert.Equal("Renamed", renamed.Name);
        var moved = Succeeded(await folders.MoveFolderAsync(child.Id, new MoveFolderRequestDto { ParentId = null }, cancellationToken));
        Assert.Null(moved.ParentId);

        var created = Succeeded(await secrets.CreateSecretAsync(
            new CreateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = "Round trip login",
                Username = "ada",
                Url = "https://example.test/login",
                Value = FirstValue,
                Notes = NotesText,
                Tags = ["alpha", "beta"],
                FolderId = parent.Id,
            },
            cancellationToken));
        Assert.Equal(parent.Id, created.FolderId);
        Assert.True(created.HasValue);

        var inFolder = Succeeded(await secrets.ListSecretsAsync(parent.Id, false, null, 50, null, cancellationToken));
        Assert.Equal([created.Id], inFolder.Items.Select(item => item.Id));
        var found = Succeeded(await secrets.ListSecretsAsync(null, false, "Round trip", 50, null, cancellationToken));
        Assert.Contains(found.Items, item => item.Id == created.Id);
        var unfiled = Succeeded(await secrets.ListSecretsAsync(null, true, null, 50, null, cancellationToken));
        Assert.DoesNotContain(unfiled.Items, item => item.Id == created.Id);
        var fetched = Succeeded(await secrets.GetSecretAsync(created.Id, cancellationToken));
        Assert.Equal(created.ConcurrencyToken, fetched.ConcurrencyToken);

        var viewed = Succeeded(await secrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken));
        Assert.Equal(FirstValue, viewed.Value);
        var notes = Succeeded(await secrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.Copy }, cancellationToken));
        Assert.Equal(NotesText, notes.Value);

        var updated = Succeeded(await secrets.UpdateSecretAsync(
            created.Id,
            created.ConcurrencyToken,
            new UpdateSecretRequestDto
            {
                Type = SecretTypeDto.Login,
                Name = "Round trip login v2",
                Username = "ada",
                Url = "https://example.test/login",
                Tags = ["alpha"],
                ReplaceValue = true,
                Value = SecondValue,
            },
            cancellationToken));
        Assert.Equal("Round trip login v2", updated.Name);
        Assert.NotEqual(created.ConcurrencyToken, updated.ConcurrencyToken);

        var stale = await secrets.UpdateSecretAsync(created.Id, created.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "stale" }, cancellationToken);
        Assert.Equal(ApiCallStatus.Conflict, stale.Status);

        var movedSecret = Succeeded(await secrets.MoveSecretAsync(created.Id, updated.ConcurrencyToken, new MoveSecretRequestDto { FolderId = null }, cancellationToken));
        Assert.Null(movedSecret.FolderId);

        var versions = Succeeded(await history.ListVersionsAsync(created.Id, cancellationToken));
        var version = Assert.Single(versions.Items);
        var oldValue = Succeeded(await history.RevealVersionAsync(created.Id, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View }, cancellationToken));
        Assert.Equal(FirstValue, oldValue.Value);
        var restored = Succeeded(await history.RestoreVersionAsync(created.Id, version.VersionId, movedSecret.ConcurrencyToken, cancellationToken));
        var restoredValue = Succeeded(await secrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken));
        Assert.Equal(FirstValue, restoredValue.Value);

        var deleted = await secrets.DeleteSecretAsync(created.Id, restored.ConcurrencyToken, cancellationToken);
        Assert.Equal(ApiCallStatus.Success, deleted.Status);
        Assert.True(deleted.Value);
        Assert.Equal(ApiCallStatus.NotFound, (await secrets.GetSecretAsync(created.Id, cancellationToken)).Status);

        var binned = Succeeded(await bin.ListAsync(50, null, cancellationToken));
        Assert.Contains(binned.Items, item => item.Id == created.Id && item.Kind == RecycleBinItemKindDto.Secret);
        var back = Succeeded(await bin.RestoreSecretAsync(created.Id, cancellationToken));
        Assert.Equal(created.Id, back.Id);
        var deletedAgain = await secrets.DeleteSecretAsync(created.Id, back.ConcurrencyToken, cancellationToken);
        Assert.True(deletedAgain.Value);
        var purgedSecret = await bin.PurgeSecretAsync(created.Id, cancellationToken);
        Assert.Equal(ApiCallStatus.Success, purgedSecret.Status);
        Assert.True(purgedSecret.Value);
        Assert.Equal(ApiCallStatus.NotFound, (await bin.RestoreSecretAsync(created.Id, cancellationToken)).Status);

        var folderDeleted = await folders.DeleteFolderAsync(parent.Id, FolderDeleteModeDto.DeleteContents, cancellationToken);
        Assert.True(folderDeleted.Value);
        var binnedFolders = Succeeded(await bin.ListAsync(50, null, cancellationToken));
        Assert.Contains(binnedFolders.Items, item => item.Id == parent.Id && item.Kind == RecycleBinItemKindDto.Folder);
        var folderBack = Succeeded(await bin.RestoreFolderAsync(parent.Id, cancellationToken));
        Assert.Equal(parent.Id, folderBack.Id);
        var movedOut = await folders.DeleteFolderAsync(parent.Id, FolderDeleteModeDto.MoveToParent, cancellationToken);
        Assert.True(movedOut.Value);
        var purgedFolder = await bin.PurgeFolderAsync(parent.Id, cancellationToken);
        Assert.Equal(ApiCallStatus.Success, purgedFolder.Status);
        Assert.True(purgedFolder.Value);

        Assert.Empty(_api.StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_RealTokenIdentifiesTheCircuitUser_OtherUsersSeeNothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = WebCircuit.For(_web, TestUsers.User);
        using var other = WebCircuit.For(_web, TestUsers.OtherUser);
        var created = Succeeded(await owner.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Owned by user", Value = FirstValue },
            cancellationToken));
        var folder = Succeeded(await owner.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Owned folder" }, cancellationToken));

        using var direct = _api.CreateAuthenticatedClient(TestUsers.User);
        var directList = await direct.GetFromJsonAsync<SecretPageDto>(ApiRoutes.VaultSecrets, cancellationToken);
        Assert.Contains(directList!.Items, item => item.Id == created.Id);

        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultSecrets.GetSecretAsync(created.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultSecrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.SecretHistory.ListVersionsAsync(created.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultSecrets.DeleteSecretAsync(created.Id, created.ConcurrencyToken, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultFolders.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "taken" }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.RecycleBin.PurgeSecretAsync(created.Id, cancellationToken)).Status);
        Assert.Empty(Succeeded(await other.VaultSecrets.ListSecretsAsync(null, false, null, 50, null, cancellationToken)).Items);
        Assert.Empty(Succeeded(await other.VaultFolders.ListFoldersAsync(cancellationToken)).Items);
        Assert.Equal(FirstValue, Succeeded(await owner.VaultSecrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Value);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("NoRole")]
    [InlineData("Guest")]
    public async Task VaultClients_CallerWithoutAVaultRole_GetsAccessDeniedOnEveryClient(string caller)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var user = caller == "Guest" ? TestUsers.Guest : TestUsers.AuthenticatedWithoutRole;
        using var circuit = WebCircuit.For(_web, user);

        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultSecrets.ListSecretsAsync(null, false, null, 50, null, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultFolders.ListFoldersAsync(cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.RecycleBin.ListAsync(50, null, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.SecretHistory.ListVersionsAsync(Guid.NewGuid(), cancellationToken)).Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_Calls_LogNoSecretValueOrNotes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);
        var created = Succeeded(await circuit.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Logged", Value = FirstValue, Notes = NotesText },
            cancellationToken));
        Succeeded(await circuit.VaultSecrets.RevealSecretAsync(created.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy }, cancellationToken));
        await circuit.VaultSecrets.UpdateSecretAsync(created.Id, "stale-token", new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "x", ReplaceValue = true, Value = SecondValue }, cancellationToken);

        foreach (var marker in new[] { FirstValue, SecondValue, NotesText })
        {
            Assert.False(_web.Logs.ContainsText(marker), "web log contains " + marker);
            Assert.False(_api.Logs.ContainsText(marker), "api log contains " + marker);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_StaleToken_ReturnsConflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);
        var created = Succeeded(await circuit.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Stale", Value = FirstValue },
            cancellationToken));
        var updated = Succeeded(await circuit.VaultSecrets.UpdateSecretAsync(
            created.Id,
            created.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Fresh", ReplaceValue = true, Value = SecondValue },
            cancellationToken));
        var folder = Succeeded(await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Target" }, cancellationToken));
        var version = Assert.Single(Succeeded(await circuit.SecretHistory.ListVersionsAsync(created.Id, cancellationToken)).Items);

        var staleUpdate = await circuit.VaultSecrets.UpdateSecretAsync(created.Id, created.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Lost" }, cancellationToken);
        var staleMove = await circuit.VaultSecrets.MoveSecretAsync(created.Id, created.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id }, cancellationToken);
        var staleRestore = await circuit.SecretHistory.RestoreVersionAsync(created.Id, version.VersionId, created.ConcurrencyToken, cancellationToken);
        var staleDelete = await circuit.VaultSecrets.DeleteSecretAsync(created.Id, created.ConcurrencyToken, cancellationToken);

        Assert.Equal(ApiCallStatus.Conflict, staleUpdate.Status);
        Assert.Equal(ApiCallStatus.Conflict, staleMove.Status);
        Assert.Equal(ApiCallStatus.Conflict, staleRestore.Status);
        Assert.Equal(ApiCallStatus.Conflict, staleDelete.Status);
        var unchanged = Succeeded(await circuit.VaultSecrets.GetSecretAsync(created.Id, cancellationToken));
        Assert.Equal("Fresh", unchanged.Name);
        Assert.Null(unchanged.FolderId);
        Assert.Equal(updated.ConcurrencyToken, unchanged.ConcurrencyToken);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_OtherUsersId_ReturnsNotFound()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = WebCircuit.For(_web, TestUsers.User);
        using var other = WebCircuit.For(_web, TestUsers.OtherUser);
        var secret = Succeeded(await owner.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Mine", Value = FirstValue },
            cancellationToken));
        var folder = Succeeded(await owner.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Mine" }, cancellationToken));
        var ownSecret = Succeeded(await other.VaultSecrets.CreateSecretAsync(
            new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Theirs", Value = SecondValue },
            cancellationToken));
        var version = Succeeded(await owner.VaultSecrets.UpdateSecretAsync(
            secret.Id,
            secret.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Mine v2", ReplaceValue = true, Value = SecondValue },
            cancellationToken));
        var versionId = Assert.Single(Succeeded(await owner.SecretHistory.ListVersionsAsync(secret.Id, cancellationToken)).Items).VersionId;
        Succeeded(await owner.VaultSecrets.DeleteSecretAsync(secret.Id, version.ConcurrencyToken, cancellationToken));

        Assert.Equal(ApiCallStatus.NotFound, (await other.SecretHistory.RevealVersionAsync(secret.Id, versionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.SecretHistory.RestoreVersionAsync(secret.Id, versionId, version.ConcurrencyToken, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.RecycleBin.RestoreSecretAsync(secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultSecrets.MoveSecretAsync(ownSecret.Id, ownSecret.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultFolders.MoveFolderAsync(folder.Id, new MoveFolderRequestDto { ParentId = null }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.VaultFolders.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.RecycleBin.RestoreFolderAsync(folder.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.NotFound, (await other.RecycleBin.PurgeFolderAsync(folder.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.Success, (await owner.RecycleBin.RestoreSecretAsync(secret.Id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.Success, (await owner.VaultFolders.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "Still mine" }, cancellationToken)).Status);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task VaultClients_RejectedFolderMove_ReturnsInvalidWithDetail()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var circuit = WebCircuit.For(_web, TestUsers.User);
        var parent = Succeeded(await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Parent" }, cancellationToken));
        var child = Succeeded(await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Child", ParentId = parent.Id }, cancellationToken));

        var underItself = await circuit.VaultFolders.MoveFolderAsync(parent.Id, new MoveFolderRequestDto { ParentId = child.Id }, cancellationToken);
        var intoSelf = await circuit.VaultFolders.MoveFolderAsync(parent.Id, new MoveFolderRequestDto { ParentId = parent.Id }, cancellationToken);

        foreach (var rejected in new[] { underItself, intoSelf })
        {
            Assert.Equal(ApiCallStatus.Invalid, rejected.Status);
            Assert.Null(rejected.Value);
            Assert.False(string.IsNullOrWhiteSpace(rejected.Detail));
        }

        var listed = Succeeded(await circuit.VaultFolders.ListFoldersAsync(cancellationToken));
        Assert.Null(listed.Items.Single(folder => folder.Id == parent.Id).ParentId);
        Assert.Equal(parent.Id, listed.Items.Single(folder => folder.Id == child.Id).ParentId);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("LockedUser")]
    [InlineData("LockedAdmin")]
    public async Task VaultClients_LockedUser_ReturnsAccessDenied(string name)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var locked = name == "LockedAdmin" ? TestUsers.LockedAdmin : TestUsers.LockedUser;
        await _api.SeedUserAsync(locked, UserRows.LockedStatus, DateTimeOffset.UtcNow, cancellationToken);
        using var circuit = WebCircuit.For(_web, locked);
        var id = Guid.NewGuid();
        _api.StorageRequests.Clear();

        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultSecrets.ListSecretsAsync(null, false, null, 50, null, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultSecrets.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Locked", Value = FirstValue }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultSecrets.RevealSecretAsync(id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultFolders.CreateFolderAsync(new CreateFolderRequestDto { Name = "Locked" }, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.VaultFolders.ListFoldersAsync(cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.SecretHistory.ListVersionsAsync(id, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.RecycleBin.ListAsync(50, null, cancellationToken)).Status);
        Assert.Equal(ApiCallStatus.AccessDenied, (await circuit.RecycleBin.PurgeSecretAsync(id, cancellationToken)).Status);
        Assert.Empty(_api.StorageRequests.VaultRequests());
    }

    private static T Succeeded<T>(ApiResponse<T> response)
    {
        Assert.Equal(ApiCallStatus.Success, response.Status);
        return response.Value!;
    }
}
