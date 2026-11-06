// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class SecretAuditLogTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const string GuidFormat = "D";

    [Fact]
    public async Task SecretEvent_WritesIndexRowInOwnerPartitionWithSecretPrefix()
    {
        var secret = await NewSecretAsync();

        var rows = await SecretAuditRowsAsync(Caller.ObjectId, secret.Id);

        var row = Assert.Single(rows);
        Assert.Equal(SecretAuditKeys.PartitionKey(Caller.ObjectId), row.PartitionKey);
        Assert.StartsWith($"{secret.Id:N}_", row.RowKey, StringComparison.Ordinal);
        Assert.Equal(nameof(AuditEventType.SecretCreated), row.GetString("EventType"));
        Assert.Equal(secret.Id.ToString(GuidFormat), row.GetString("SecretId"));
        Assert.Equal(Caller.ObjectId.ToString(GuidFormat), row.GetString("OwnerId"));
        Assert.Equal(Caller.ObjectId.ToString(GuidFormat), row.GetString("ActorId"));
        Assert.Equal(secret.Id.ToString(GuidFormat), row.GetString("TargetId"));
        var main = await VaultAuditRowsAsync(AuditEventType.SecretCreated);
        Assert.Single(main);
    }

    [Fact]
    public async Task SecretEvent_OtherSecretsRowsAreNotMixedIn()
    {
        var first = await NewSecretAsync();
        var second = await NewSecretAsync();

        var firstRows = await SecretAuditRowsAsync(Caller.ObjectId, first.Id);
        var secondRows = await SecretAuditRowsAsync(Caller.ObjectId, second.Id);

        Assert.Single(firstRows);
        Assert.Single(secondRows);
        Assert.All(firstRows, row => Assert.Equal(first.Id.ToString(GuidFormat), row.GetString("SecretId")));
        Assert.All(secondRows, row => Assert.Equal(second.Id.ToString(GuidFormat), row.GetString("SecretId")));
    }

    [Fact]
    public async Task FolderEvent_WritesNoIndexRow()
    {
        var folder = await NewFolderAsync("Audited folder");
        Require(await Api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "Renamed" }));

        var indexRows = new List<Azure.Data.Tables.TableEntity>();
        await foreach (var row in GetTableClient(TableNames.SecretAuditLog).QueryAsync<Azure.Data.Tables.TableEntity>(cancellationToken: Token))
        {
            indexRows.Add(row);
        }

        Assert.Empty(indexRows);
        var main = await VaultAuditRowsAsync(AuditEventType.FolderCreated, AuditEventType.FolderRenamed);
        Assert.Equal(2, main.Count);
    }

    [Fact]
    public async Task IndexRows_SortNewestFirstPerSecret()
    {
        var secret = await NewSecretAsync();
        await RevealValueAsync(secret.Id);
        var updated = await ReplaceValueAsync(secret, "second");
        var folder = await NewFolderAsync("Destination");
        Require(await Api.MoveSecretAsync(secret.Id, updated.ConcurrencyToken, new MoveSecretRequestDto { FolderId = folder.Id }));

        var rows = await SecretAuditRowsAsync(Caller.ObjectId, secret.Id);

        Assert.Equal(
            [
                nameof(AuditEventType.SecretMoved),
                nameof(AuditEventType.SecretUpdated),
                nameof(AuditEventType.SecretValueRevealed),
                nameof(AuditEventType.SecretCreated),
            ],
            rows.Select(row => row.GetString("EventType")));
        var times = rows.Select(row => row.GetDateTimeOffset("OccurredAt")!.Value).ToList();
        Assert.Equal(times.OrderByDescending(time => time), times);
    }

    [Fact]
    public async Task IndexWriteFails_RevealReturns500WithoutValue()
    {
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(Api, markers);
        await Factory.StorageAdminClient.DeleteTableAsync(TableNames.SecretAuditLog, Token);

        var response = await Api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });

        Assert.Equal(HttpStatusCode.InternalServerError, response.Response.StatusCode);
        Assert.Null(response.Body);
        var text = await response.Response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(markers.Value, text, StringComparison.Ordinal);
        AssertLogsDoNotContain(markers.Value);
    }
}
