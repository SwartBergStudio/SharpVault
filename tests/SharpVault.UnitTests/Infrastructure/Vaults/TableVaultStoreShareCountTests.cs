// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Core;
using Azure.Data.Tables;
using NSubstitute;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Trait("Category", "Security")]
public sealed class TableVaultStoreShareCountTests
{
    private const int ManyGrants = 1001;
    private const string NextPageToken = "next-grant-page";
    private const string GrantRowPrefix = "gi_";
    private const string RowKeyColumn = "RowKey";

    private readonly TableClient _table = Substitute.For<TableClient>();
    private readonly TableVaultStore _store;
    private readonly Guid _owner = Guid.NewGuid();

    public TableVaultStoreShareCountTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Vaults).Returns(_table);
        _store = new TableVaultStore(service, new CollectingLogger<TableVaultStore>());
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CountShareGrantsByOwnerAsync_NoGrants_ReturnsZero()
    {
        ReturnsPages(Page<TableEntity>.FromValues([], null, Substitute.For<Response>()));

        var count = await _store.CountShareGrantsByOwnerAsync(_owner, 1, Token);

        Assert.Equal(0, count.Count);
        Assert.False(count.IsCapped);
    }

    [Fact]
    public async Task CountShareGrantsByOwnerAsync_MaxPagesReached_ReportsCapped()
    {
        ReturnsPages(Page<TableEntity>.FromValues(GrantRows(ManyGrants), NextPageToken, Substitute.For<Response>()));

        var count = await _store.CountShareGrantsByOwnerAsync(_owner, 1, Token);

        Assert.Equal(ManyGrants, count.Count);
        Assert.True(count.IsCapped);
    }

    [Fact]
    public async Task CountShareGrantsByOwnerAsync_SelectsOnlyTheRowKey()
    {
        ReturnsPages(Page<TableEntity>.FromValues([], null, Substitute.For<Response>()));

        await _store.CountShareGrantsByOwnerAsync(_owner, 1, Token);

        _ = _table.Received(1).QueryAsync<TableEntity>(
            Arg.Any<string>(),
            Arg.Any<int?>(),
            Arg.Is<IEnumerable<string>>(columns => columns.SequenceEqual(new[] { RowKeyColumn })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CountShareGrantsByOwnerAsync_DamagedRow_StillCounts()
    {
        var damaged = new TableEntity(VaultKeys.PartitionKey(_owner), GrantRowPrefix + "damaged-row");
        var healthy = new TableEntity(VaultKeys.PartitionKey(_owner), GrantRowPrefix + Guid.NewGuid().ToString("N"));
        ReturnsPages(Page<TableEntity>.FromValues([damaged, healthy], null, Substitute.For<Response>()));

        var count = await _store.CountShareGrantsByOwnerAsync(_owner, 1, Token);

        Assert.Equal(2, count.Count);
        Assert.False(count.IsCapped);
    }

    private void ReturnsPages(params Page<TableEntity>[] pages) =>
        _table.QueryAsync<TableEntity>(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(AsyncPageable<TableEntity>.FromPages(pages));

    private List<TableEntity> GrantRows(int count) =>
        Enumerable.Range(0, count)
            .Select(_ => new TableEntity(VaultKeys.PartitionKey(_owner), GrantRowPrefix + Guid.NewGuid().ToString("N")))
            .ToList();
}
