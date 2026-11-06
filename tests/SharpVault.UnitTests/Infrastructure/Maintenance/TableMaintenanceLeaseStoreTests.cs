// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Domain.Maintenance;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Infrastructure.Maintenance;

public sealed class TableMaintenanceLeaseStoreTests
{
    private const string Name = MaintenanceKeys.RecycleBinPurgeLease;
    private const string Holder = "instance-a";
    private const string Other = "instance-b";
    private const string ETagValue = "W/\"datetime'2026-03-01T09%3A00%3A00.0000000Z'\"";

    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(5);

    private readonly FakeLeaseTableClient _table = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly TableMaintenanceLeaseStore _store;

    public TableMaintenanceLeaseStoreTests()
    {
        var service = Substitute.For<TableServiceClient>();
        service.GetTableClient(TableNames.Maintenance).Returns(_table);
        _store = new TableMaintenanceLeaseStore(service, _time);
    }

    private static MaintenanceLease Lease(string holder) => new() { Name = Name, Holder = holder, ExpiresAt = DateTimeOffset.UnixEpoch };

    private MaintenanceLeaseTableEntity Row(string holder, TimeSpan expiresIn) => new()
    {
        PartitionKey = MaintenanceKeys.PartitionKey,
        RowKey = Name,
        Holder = holder,
        ExpiresAt = _time.GetUtcNow() + expiresIn,
        RunId = Guid.NewGuid().ToString("D"),
        AfterUserId = Guid.NewGuid().ToString("D"),
        RunStartedAt = _time.GetUtcNow().AddHours(-1),
        ETag = new ETag(ETagValue),
    };

    [Fact]
    public async Task TryAcquire_NoRow_Adds()
    {
        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("W/\"added\"", result.ETag);
        Assert.Equal(Holder, result.Value.Holder);
        Assert.Equal(_time.GetUtcNow() + Duration, result.Value.ExpiresAt);
        var added = Assert.Single(_table.Added);
        Assert.Equal(MaintenanceKeys.PartitionKey, added.PartitionKey);
        Assert.Equal(Name, added.RowKey);
        Assert.Empty(_table.Updated);
    }

    [Fact]
    public async Task TryAcquire_NoRowAndAddConflicts_ReturnsNull()
    {
        _table.OnAdd = () => throw new RequestFailedException(409, "exists");

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryAcquire_HeldByOtherNotExpired_ReturnsNull()
    {
        _table.Row = Row(Other, TimeSpan.FromMinutes(1));

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.Null(result);
        Assert.Empty(_table.Updated);
        Assert.Empty(_table.Added);
    }

    [Fact]
    public async Task TryAcquire_HeldByOtherExactlyAtExpiry_Replaces()
    {
        _table.Row = Row(Other, TimeSpan.Zero);

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(Holder, result.Value.Holder);
    }

    [Fact]
    public async Task TryAcquire_Expired_ReplacesWithIfMatchAndKeepsRunState()
    {
        var row = Row(Other, TimeSpan.FromMinutes(-1));
        var runId = row.RunId;
        var afterUser = row.AfterUserId;
        var runStarted = row.RunStartedAt;
        _table.Row = row;

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("W/\"updated\"", result.ETag);
        Assert.Equal(Holder, result.Value.Holder);
        Assert.Equal(_time.GetUtcNow() + Duration, result.Value.ExpiresAt);
        Assert.Equal(runId, result.Value.RunId?.ToString("D"));
        Assert.Equal(afterUser, result.Value.AfterUserId?.ToString("D"));
        Assert.Equal(runStarted, result.Value.RunStartedAt);
        var update = Assert.Single(_table.Updated);
        Assert.Equal(new ETag(ETagValue), update.IfMatch);
        Assert.Equal(TableUpdateMode.Replace, update.Mode);
        Assert.Equal(Holder, update.Entity.Holder);
    }

    [Fact]
    public async Task TryAcquire_HeldBySameHolderNotExpired_Replaces()
    {
        _table.Row = Row(Holder, TimeSpan.FromMinutes(1));

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal(_time.GetUtcNow() + Duration, result.Value.ExpiresAt);
    }

    [Fact]
    public async Task TryAcquire_PreconditionFailed_ReturnsNull()
    {
        _table.Row = Row(Other, TimeSpan.FromMinutes(-1));
        _table.OnUpdate = () => throw new RequestFailedException(412, "stale");

        var result = await _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryAcquire_OtherStorageFailure_Propagates()
    {
        _table.Row = Row(Other, TimeSpan.FromMinutes(-1));
        _table.OnUpdate = () => throw new RequestFailedException(500, "boom");

        await Assert.ThrowsAsync<RequestFailedException>(() => _store.TryAcquireAsync(Name, Holder, Duration, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquire_BlankNameOrHolder_Throws(string blank)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.TryAcquireAsync(blank, Holder, Duration, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.TryAcquireAsync(Name, blank, Duration, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task TryAcquire_NonPositiveDuration_Throws(int minutes)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.TryAcquireAsync(Name, Holder, TimeSpan.FromMinutes(minutes), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Renew_Held_ReplacesWithIfMatchAndNewExpiry()
    {
        _table.OnUpdate = () => new FakeAzureResponse(204, "W/\"renewed\"");

        var result = await _store.RenewAsync(Lease(Holder), ETagValue, Duration, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("W/\"renewed\"", result.ETag);
        Assert.Equal(_time.GetUtcNow() + Duration, result.Value.ExpiresAt);
        var update = Assert.Single(_table.Updated);
        Assert.Equal(new ETag(ETagValue), update.IfMatch);
        Assert.Equal(TableUpdateMode.Replace, update.Mode);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(404)]
    public async Task Renew_Lost_ReturnsNull(int status)
    {
        _table.OnUpdate = () => throw new RequestFailedException(status, "lost");

        var result = await _store.RenewAsync(Lease(Holder), ETagValue, Duration, TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task Renew_NullLeaseOrBlankETag_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _store.RenewAsync(null!, ETagValue, Duration, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _store.RenewAsync(Lease(Holder), " ", Duration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Release_Held_ExpiresTheLeaseNowWithIfMatch()
    {
        await _store.ReleaseAsync(Lease(Holder), ETagValue, TestContext.Current.CancellationToken);

        var update = Assert.Single(_table.Updated);
        Assert.Equal(_time.GetUtcNow(), update.Entity.ExpiresAt);
        Assert.Equal(new ETag(ETagValue), update.IfMatch);
    }

    [Fact]
    public async Task Release_Lost_DoesNotThrow()
    {
        _table.OnUpdate = () => throw new RequestFailedException(412, "lost");

        await _store.ReleaseAsync(Lease(Holder), ETagValue, TestContext.Current.CancellationToken);

        Assert.Single(_table.Updated);
    }
}
