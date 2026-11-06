// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultTamperingTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const string NameEnvelope = "NameEnvelope";
    private const string UsernameEnvelope = "UsernameEnvelope";
    private const string ValueEnvelope = "ValueEnvelope";
    private const string NotesEnvelope = "NotesEnvelope";
    private const string WrappedDataKey = "WrappedDataKey";
    private const string DecryptionTitle = "The item could not be read.";

    private static readonly Guid Owner = TestUsers.User.ObjectId;

    private static string SecretRow(Guid id) => VaultKeys.SecretRowKey(id, recycled: false);

    private static async Task AssertGenericDecryptionFailureAsync(HttpResponseMessage response, PlaintextMarkers markers)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await BodyOfAsync(response);
        var problem = await ProblemOfAsync(response);
        Assert.Equal(DecryptionTitle, problem.GetProperty("title").GetString());
        Assert.False(problem.TryGetProperty("detail", out _));
        markers.AssertAbsentFrom(text);
    }

    [Fact]
    public async Task GetSecret_NameAndUsernameEnvelopesSwapped_ReturnsDamagedWithoutPlaintext()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        await VaultRows.SwapPropertiesAsync(VaultsTable, Owner, SecretRow(secret.Id), NameEnvelope, UsernameEnvelope, Token);

        var result = await api.GetSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.True(result.Body!.IsDamaged);
        Assert.Null(result.Body.Name);
        Assert.Null(result.Body.Username);
        Assert.Null(result.Body.Url);
        Assert.Empty(result.Body.Tags);
        markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task UpdateSecret_DamagedMetadata_Returns400AndLeavesTheRowUntouched()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        await VaultRows.SwapPropertiesAsync(VaultsTable, Owner, SecretRow(secret.Id), NameEnvelope, UsernameEnvelope, Token);
        var before = await VaultRows.GetAsync(VaultsTable, Owner, SecretRow(secret.Id), Token);
        var damaged = Require(await api.GetSecretAsync(secret.Id));

        var result = await api.UpdateSecretAsync(secret.Id, damaged.ConcurrencyToken, UpdateRequest(new PlaintextMarkers(), "replacement"));

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        var after = await VaultRows.GetAsync(VaultsTable, Owner, SecretRow(secret.Id), Token);
        Assert.Equal(before!.ETag, after!.ETag);
        foreach (var property in new[] { NameEnvelope, UsernameEnvelope, ValueEnvelope, NotesEnvelope, WrappedDataKey })
        {
            Assert.Equal(before.GetBinary(property), after.GetBinary(property));
        }

        markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task RevealValue_NotesEnvelopeCopiedIntoValue_Returns500GenericBody()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var row = await VaultRows.GetAsync(VaultsTable, Owner, SecretRow(secret.Id), Token);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner, SecretRow(secret.Id), ValueEnvelope, row![NotesEnvelope], Token);

        var response = (await api.RevealSecretAsync(secret.Id, ViewValue)).Response;

        await AssertGenericDecryptionFailureAsync(response, markers);
    }

    [Fact]
    public async Task RevealNotes_ValueEnvelopeCopiedIntoNotes_Returns500()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var row = await VaultRows.GetAsync(VaultsTable, Owner, SecretRow(secret.Id), Token);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner, SecretRow(secret.Id), NotesEnvelope, row![ValueEnvelope], Token);

        var response = (await api.RevealSecretAsync(secret.Id, ViewNotes)).Response;

        await AssertGenericDecryptionFailureAsync(response, markers);
    }

    [Fact]
    public async Task GetSecret_NameEnvelopeFromAnotherSecret_ReturnsDamaged()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var first = await CreateSecretAsync(api, markers);
        var second = await CreateSecretAsync(api, new PlaintextMarkers());
        await VaultRows.CopyPropertyAsync(VaultsTable, Owner, SecretRow(second.Id), SecretRow(first.Id), NameEnvelope, Token);

        var result = await api.GetSecretAsync(first.Id);

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.True(result.Body!.IsDamaged);
        Assert.Null(result.Body.Name);
        Assert.False((await api.GetSecretAsync(second.Id)).Body!.IsDamaged);
    }

    [Fact]
    public async Task RevealValue_EnvelopeAndKeyFromAnotherSecret_Returns500()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var first = await CreateSecretAsync(api, markers);
        var otherMarkers = new PlaintextMarkers();
        var second = await CreateSecretAsync(api, otherMarkers);
        await VaultRows.CopyPropertyAsync(VaultsTable, Owner, SecretRow(second.Id), SecretRow(first.Id), ValueEnvelope, Token);
        await VaultRows.CopyPropertyAsync(VaultsTable, Owner, SecretRow(second.Id), SecretRow(first.Id), WrappedDataKey, Token);

        var response = (await api.RevealSecretAsync(first.Id, ViewValue)).Response;

        await AssertGenericDecryptionFailureAsync(response, markers);
        otherMarkers.AssertAbsentFrom(await BodyOfAsync(response));
        Assert.Equal(otherMarkers.Value, await RevealValueAsync(api, second.Id));
    }

    [Fact]
    public async Task ListFolders_NameEnvelopeFromAnotherFolder_ReturnsDamaged()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var first = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-1" }));
        var second = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-2" }));
        await VaultRows.CopyPropertyAsync(VaultsTable, Owner, VaultKeys.FolderRowKey(second.Id, recycled: false), VaultKeys.FolderRowKey(first.Id, recycled: false), NameEnvelope, Token);

        var result = await api.ListFoldersAsync();

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        var damaged = Assert.Single(result.Body!.Items, folder => folder.IsDamaged);
        Assert.Equal(first.Id, damaged.Id);
        Assert.Null(damaged.Name);
        Assert.Equal(markers.Folder + "-2", Assert.Single(result.Body.Items, folder => !folder.IsDamaged).Name);
    }

    [Fact]
    public async Task RenameFolder_DamagedName_Returns400AndLeavesTheRowUntouched()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var first = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-1" }));
        var second = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-2" }));
        var firstRow = VaultKeys.FolderRowKey(first.Id, recycled: false);
        await VaultRows.CopyPropertyAsync(VaultsTable, Owner, VaultKeys.FolderRowKey(second.Id, recycled: false), firstRow, NameEnvelope, Token);
        var before = await VaultRows.GetAsync(VaultsTable, Owner, firstRow, Token);

        var result = await api.RenameFolderAsync(first.Id, new RenameFolderRequestDto { Name = "Replacement" });

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        var after = await VaultRows.GetAsync(VaultsTable, Owner, firstRow, Token);
        Assert.Equal(before!.ETag, after!.ETag);
        Assert.Equal(before.GetBinary(NameEnvelope), after.GetBinary(NameEnvelope));
    }

    [Fact]
    public async Task RowCopiedToOtherOwnersPartition_ListShowsDamagedAndRevealReturns500()
    {
        var ownerApi = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(ownerApi, markers);
        var otherApi = ApiFor(TestUsers.OtherUser);
        var own = await CreateSecretAsync(otherApi, new PlaintextMarkers());
        await VaultRows.CopyRowToPartitionAsync(VaultsTable, Owner, SecretRow(secret.Id), TestUsers.OtherUser.ObjectId, Token);

        var listed = await otherApi.ListSecretsAsync(pageSize: 200);
        var reveal = (await otherApi.RevealSecretAsync(secret.Id, ViewValue)).Response;

        Assert.Equal(HttpStatusCode.OK, listed.Response.StatusCode);
        Assert.Equal(2, listed.Body!.Items.Count);
        Assert.True(Assert.Single(listed.Body.Items, item => item.Id == secret.Id).IsDamaged);
        Assert.False(Assert.Single(listed.Body.Items, item => item.Id == own.Id).IsDamaged);
        markers.AssertAbsentFrom(await BodyOfAsync(listed.Response));
        await AssertGenericDecryptionFailureAsync(reveal, markers);
    }

    [Fact]
    public async Task FolderRowCopiedToOtherOwnersPartition_ReadsAsDamaged()
    {
        var ownerApi = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var folder = Require(await ownerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var otherApi = ApiFor(TestUsers.OtherUser);
        var own = Require(await otherApi.CreateFolderAsync(new CreateFolderRequestDto { Name = "own" }));
        await VaultRows.CopyRowToPartitionAsync(VaultsTable, Owner, VaultKeys.FolderRowKey(folder.Id, recycled: false), TestUsers.OtherUser.ObjectId, Token);

        var result = await otherApi.ListFoldersAsync();

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.True(Assert.Single(result.Body!.Items, item => item.Id == folder.Id).IsDamaged);
        Assert.False(Assert.Single(result.Body.Items, item => item.Id == own.Id).IsDamaged);
        markers.AssertAbsentFrom(await BodyOfAsync(result.Response));
    }

    [Fact]
    public async Task RevealValue_HistoryEnvelopeCopiedIntoValue_Returns500()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        await VaultRows.CopyPropertyAsync(
            VaultsTable,
            Owner,
            VaultKeys.VersionRowKey(scenario.SecretS, scenario.VersionV),
            SecretRow(scenario.SecretS),
            ValueEnvelope,
            Token);

        var response = (await api.RevealSecretAsync(scenario.SecretS, ViewValue)).Response;

        await AssertGenericDecryptionFailureAsync(response, scenario.Markers);
    }

    [Fact]
    public async Task HistoryReveal_RowCopiedFromAnotherSecret_Returns500()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        var otherMarkers = new PlaintextMarkers();
        var other = await CreateSecretAsync(api, otherMarkers);
        Require(await api.UpdateSecretAsync(other.Id, other.ConcurrencyToken, UpdateRequest(otherMarkers, otherMarkers.Value + "-v2")));
        var otherVersion = Require(await api.ListHistoryAsync(other.Id)).Items[0].VersionId;
        var source = await VaultRows.GetAsync(VaultsTable, Owner, VaultKeys.VersionRowKey(other.Id, otherVersion), Token);
        var copy = new TableEntity(VaultKeys.PartitionKey(Owner), VaultKeys.VersionRowKey(scenario.SecretS, otherVersion));
        foreach (var property in source!)
        {
            if (property.Key is not ("PartitionKey" or "RowKey" or "Timestamp" or "odata.etag" or "SecretId"))
            {
                copy[property.Key] = property.Value;
            }
        }

        copy["SecretId"] = scenario.SecretS.ToString("D");
        await VaultsTable.UpsertEntityAsync(copy, TableUpdateMode.Replace, Token);

        var response = (await api.RevealVersionAsync(scenario.SecretS, otherVersion, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response;

        await AssertGenericDecryptionFailureAsync(response, otherMarkers);
    }
}
