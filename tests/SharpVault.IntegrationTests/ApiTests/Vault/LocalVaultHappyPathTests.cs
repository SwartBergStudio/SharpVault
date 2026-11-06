// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Authentication.Local;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(LocalModeTestGroup))]
[Trait("Category", "Security")]
public sealed class LocalVaultHappyPathTests(LocalApiTestFactory factory) : IAsyncLifetime
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await factory.ResetAndSeedAsync(CancellationToken.None);
        factory.StorageRequests.Clear();
        factory.Logs.Clear();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private VaultTestApi ApiFor(LocalAccount account) => new(factory.CreateClientForAccount(account));

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    private static CreateSecretRequestDto NewSecret(PlaintextMarkers markers, Guid? folderId = null) => new()
    {
        Type = SecretTypeDto.Login,
        Name = markers.Name,
        Username = markers.Username,
        Url = markers.Url,
        Tags = [markers.Tag],
        Value = markers.Value,
        Notes = markers.Notes,
        FolderId = folderId,
    };

    private async Task<List<TableEntity>> SecretAuditRowsAsync(LocalAccount owner, Guid secretId)
    {
        var partition = SecretAuditKeys.PartitionKey(owner.Id);
        var prefix = secretId.ToString("N");
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ge {prefix + "_"} and RowKey lt {prefix + "`"}");
        var rows = new List<TableEntity>();
        await foreach (var row in factory.StorageAdminClient.GetTableClient(TableNames.SecretAuditLog).QueryAsync<TableEntity>(filter, cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    [Fact]
    public async Task LocalUserOne_FullVaultLifecycle_Works()
    {
        var markers = new PlaintextMarkers();
        var api = ApiFor(LocalAccounts.UserOne);

        var parent = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var child = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "Nested", ParentId = parent.Id }));
        var login = Require(await api.CreateSecretAsync(NewSecret(markers, child.Id)));
        var note = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.SecureNote, Name = "Note", Notes = markers.Notes }));

        var listed = Require(await api.ListSecretsAsync(null, false, null, 50, null));
        Assert.Equal([login.Id, note.Id], listed.Items.Select(item => item.Id).Order());
        var nested = Require(await api.ListSecretsAsync(child.Id, false, null, 50, null));
        Assert.Equal(login.Id, Assert.Single(nested.Items).Id);

        var reveal = await api.RevealSecretAsync(login.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.Equal(HttpStatusCode.OK, reveal.Response.StatusCode);
        Assert.Equal(markers.Value, reveal.Body!.Value);

        var edited = Require(await api.UpdateSecretAsync(
            login.Id,
            login.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, Username = markers.Username, ReplaceValue = true, Value = markers.Value + "-v2" }));
        var history = Require(await api.ListHistoryAsync(login.Id));
        var version = Assert.Single(history.Items);
        var old = Require(await api.RevealVersionAsync(login.Id, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View }));
        Assert.Equal(markers.Value, old.Value);

        var deleted = await api.DeleteSecretAsync(login.Id, edited.ConcurrencyToken);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.GetSecretAsync(login.Id)).Response.StatusCode);
        var bin = Require(await api.ListRecycleBinAsync());
        Assert.Contains(bin.Items, item => item.Id == login.Id);

        var restored = Require(await api.RestoreRecycledSecretAsync(login.Id));
        Assert.Equal(login.Id, restored.Id);
        var back = await api.RevealSecretAsync(login.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.Equal(markers.Value + "-v2", back.Body!.Value);
        Assert.Empty(factory.StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task LocalUserOne_RevealValue_WritesAuditRowWithOwnActorId()
    {
        var markers = new PlaintextMarkers();
        var api = ApiFor(LocalAccounts.UserOne);
        var secret = Require(await api.CreateSecretAsync(NewSecret(markers)));

        var reveal = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.OK, reveal.Response.StatusCode);
        var rows = await SecretAuditRowsAsync(LocalAccounts.UserOne, secret.Id);
        var row = Assert.Single(rows, candidate => candidate.GetString("EventType") == nameof(AuditEventType.SecretValueRevealed));
        Assert.Equal(LocalAccounts.UserOne.Id.ToString("D"), row.GetString("ActorId"));
        Assert.Equal(LocalAccounts.UserOne.Id.ToString("D"), row.GetString("OwnerId"));
        Assert.Equal(secret.Id.ToString("D"), row.GetString("SecretId"));
        markers.AssertAbsentFrom(string.Join('|', row.Select(pair => pair.Value?.ToString())));
        Assert.False(factory.Logs.ContainsText(markers.Value));
    }

    [Fact]
    public async Task LocalAdminOne_OwnVault_WorksLikeUser()
    {
        var markers = new PlaintextMarkers();
        var api = ApiFor(LocalAccounts.AdminOne);

        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = Require(await api.CreateSecretAsync(NewSecret(markers, folder.Id)));
        var reveal = await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(markers.Value, reveal.Body!.Value);
        Assert.Equal(secret.Id, Assert.Single(Require(await api.ListSecretsAsync(folder.Id, false, null, 50, null)).Items).Id);
        var rows = await SecretAuditRowsAsync(LocalAccounts.AdminOne, secret.Id);
        Assert.All(rows, row => Assert.Equal(LocalAccounts.AdminOne.Id.ToString("D"), row.GetString("ActorId")));
        Assert.Empty(Require(await ApiFor(LocalAccounts.UserOne).ListSecretsAsync(null, false, null, 50, null)).Items);
    }
}
