// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using SharpVault.Domain.Audit;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

public abstract class VaultTestBase(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    protected HttpClient ClientFor(TestUser user) => Factory.CreateAuthenticatedClient(user);

    protected VaultTestApi ApiFor(TestUser user) => new(ClientFor(user));

    protected TableClient VaultsTable => GetTableClient(TableNames.Vaults);

    protected async Task WarmUpAsync(TestUser user)
    {
        using var client = ClientFor(user);
        var response = await client.GetAsync("/api/me", Token);
        response.EnsureSuccessStatusCode();
    }

    protected async Task<IReadOnlyList<TableEntity>> VaultAuditRowsAsync(params AuditEventType[] eventTypes)
    {
        var wanted = eventTypes.Select(type => type.ToString()).ToHashSet(StringComparer.Ordinal);
        var rows = await ListAuditRowsAsync();
        return [.. rows.Where(row => row.GetString("EventType") is { } name && name != nameof(AuditEventType.SignIn) && wanted.Contains(name))];
    }

    protected async Task<IReadOnlyList<TableEntity>> SecretAuditRowsAsync(Guid owner, Guid secretId)
    {
        var partition = SecretAuditKeys.PartitionKey(owner);
        var prefix = secretId.ToString("N");
        var lower = prefix + "_";
        var upper = prefix + "`";
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition} and RowKey ge {lower} and RowKey lt {upper}");
        var rows = new List<TableEntity>();
        await foreach (var row in GetTableClient(TableNames.SecretAuditLog).QueryAsync<TableEntity>(filter, cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    protected static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }

    protected static async Task<SecretDto> CreateSecretAsync(VaultTestApi api, PlaintextMarkers markers, Guid? folderId = null) =>
        Require(await api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            Value = markers.Value,
            Notes = markers.Notes,
            FolderId = folderId,
        }));

    protected void ResetCounters()
    {
        StorageRequests.Clear();
        Factory.KeyWraps.Clear();
    }
}
