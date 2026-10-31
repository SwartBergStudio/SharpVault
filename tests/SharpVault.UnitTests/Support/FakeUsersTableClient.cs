// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using SharpVault.Infrastructure.Users;

namespace SharpVault.UnitTests.Support;

internal sealed class FakeUsersTableClient : TableClient
{
    public UserTableEntity? Entity { get; set; }

    public Func<IEnumerable<Page<UserTableEntity>>> Pages { get; set; } = () => [];

    public List<(string PartitionKey, string RowKey)> PointReads { get; } = [];

    public List<(string? Filter, int? MaxPerPage)> Queries { get; } = [];

    public override Task<NullableResponse<T>> GetEntityIfExistsAsync<T>(
        string partitionKey,
        string rowKey,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        PointReads.Add((partitionKey, rowKey));
        return Task.FromResult((NullableResponse<T>)(object)new FakeNullableResponse(Entity));
    }

    public override AsyncPageable<T> QueryAsync<T>(
        string? filter = null,
        int? maxPerPage = null,
        IEnumerable<string>? select = null,
        CancellationToken cancellationToken = default)
    {
        Queries.Add((filter, maxPerPage));
        return (AsyncPageable<T>)(object)AsyncPageable<UserTableEntity>.FromPages(Pages());
    }

    private sealed class FakeNullableResponse(UserTableEntity? value) : NullableResponse<UserTableEntity>
    {
        public override bool HasValue => value is not null;

        public override UserTableEntity? Value => value;

        public override Response GetRawResponse() => throw new NotSupportedException();
    }
}
