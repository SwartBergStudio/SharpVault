// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Maintenance;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class FakeLeaseTableClient : TableClient
{
    public MaintenanceLeaseTableEntity? Row { get; set; }

    public Func<Response> OnAdd { get; set; } = () => new FakeAzureResponse(204, "W/\"added\"");

    public Func<Response> OnUpdate { get; set; } = () => new FakeAzureResponse(204, "W/\"updated\"");

    public List<MaintenanceLeaseTableEntity> Added { get; } = [];

    public List<(MaintenanceLeaseTableEntity Entity, ETag IfMatch, TableUpdateMode Mode)> Updated { get; } = [];

    public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
        string partitionKey,
        string rowKey,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult((NullableResponse<T>)(object)new FakeNullableResponse<MaintenanceLeaseTableEntity>(Row));

    public override Task<Response> AddEntityAsync<T>(T entity, CancellationToken cancellationToken = default)
    {
        Added.Add((MaintenanceLeaseTableEntity)(object)entity);
        return Task.FromResult(OnAdd());
    }

    public override Task<Response> UpdateEntityAsync<T>(T entity, ETag ifMatch, TableUpdateMode mode = TableUpdateMode.Merge, CancellationToken cancellationToken = default)
    {
        Updated.Add(((MaintenanceLeaseTableEntity)(object)entity, ifMatch, mode));
        return Task.FromResult(OnUpdate());
    }
}
