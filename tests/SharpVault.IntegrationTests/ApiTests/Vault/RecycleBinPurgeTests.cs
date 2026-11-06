// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Users;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
public sealed class RecycleBinPurgeTests(ApiTestFactory factory) : VaultFunctionalTestBase(factory)
{
    private const string GuidFormat = "D";
    private const int UserPageSize = 100;

    private static readonly TimeSpan JustUnderRetention = VaultLimits.RecycleBinRetention - TimeSpan.FromHours(1);
    private static readonly TimeSpan JustOverRetention = VaultLimits.RecycleBinRetention + TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);

    private WebApplicationFactory<Program> TimedHost(Action<IServiceCollection>? configure = null) =>
        CreateTimedFactory(_time, TimeSpan.Zero, configure);

    private static IRecycleBinPurger PurgerOf(WebApplicationFactory<Program> host) => host.Services.GetRequiredService<IRecycleBinPurger>();

    private async Task<TableEntity?> BinnedSecretRowAsync(Guid secretId) =>
        await VaultRows.GetAsync(VaultsTable, Caller.ObjectId, VaultKeys.SecretRowKey(secretId, recycled: true), Token);

    private async Task<TableEntity?> BinnedFolderRowAsync(Guid folderId) =>
        await VaultRows.GetAsync(VaultsTable, Caller.ObjectId, VaultKeys.FolderRowKey(folderId, recycled: true), Token);

    private async Task<IReadOnlyList<TableEntity>> AuditEventsAsync(AuditEventType eventType)
    {
        var filter = TableClient.CreateQueryFilter($"EventType eq {eventType.ToString()}");
        var rows = new List<TableEntity>();
        await foreach (var row in GetAuditTable().QueryAsync<TableEntity>(filter, cancellationToken: Token))
        {
            rows.Add(row);
        }

        return rows;
    }

    private async Task<SecretDto> BinnedSecretAsync()
    {
        var secret = await NewSecretAsync();
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        return secret;
    }

    private async Task AddUsersAsync(int count)
    {
        _ = Factory.Server;
        var users = Factory.Services.GetRequiredService<IUserStore>();
        for (var index = 0; index < count; index++)
        {
            var added = await users.AddAsync(
                new User
                {
                    Id = new Guid(index + 1, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]),
                    DisplayName = $"Synthetic {index}",
                    Status = UserStatus.Active,
                    CreatedAt = _time.GetUtcNow(),
                    LastSignInAt = _time.GetUtcNow(),
                },
                Token);
            Assert.Equal(StoreWriteResult.Written, added);
        }
    }

    [Fact]
    public async Task Purge_ItemDeleted29Days23HoursAgo_IsKept()
    {
        var secret = await BinnedSecretAsync();
        var folder = await NewFolderAsync("Binned");
        await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        using var host = TimedHost();
        _time.Advance(JustUnderRetention);

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.False(result.Skipped);
        Assert.Equal(0, result.SecretsPurged);
        Assert.Equal(0, result.FoldersPurged);
        Assert.NotNull(await BinnedSecretRowAsync(secret.Id));
        Assert.NotNull(await BinnedFolderRowAsync(folder.Id));
    }

    [Fact]
    public async Task Purge_ItemDeleted30DaysAndOneSecondAgo_IsPurged()
    {
        var secret = await BinnedSecretAsync();
        var folder = await NewFolderAsync("Binned");
        await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(1, result.SecretsPurged);
        Assert.Equal(1, result.FoldersPurged);
        Assert.Null(await BinnedSecretRowAsync(secret.Id));
        Assert.Null(await BinnedFolderRowAsync(folder.Id));
        Assert.Empty(await WholeBinAsync());
    }

    [Fact]
    public async Task Purge_ExpiredAndFreshItemsTogether_PurgesOnlyTheExpiredOnes()
    {
        var expired = await BinnedSecretAsync();
        using var host = TimedHost();
        _time.Advance(VaultLimits.RecycleBinRetention - TimeSpan.FromHours(1));
        await VaultRows.SetDeletedAtAsync(VaultsTable, Caller.ObjectId, VaultKeys.SecretRowKey(expired.Id, recycled: true), _time.GetUtcNow() - VaultLimits.RecycleBinRetention - TimeSpan.FromMinutes(1), Token);
        var fresh = await BinnedSecretAsync();
        await VaultRows.SetDeletedAtAsync(VaultsTable, Caller.ObjectId, VaultKeys.SecretRowKey(fresh.Id, recycled: true), _time.GetUtcNow(), Token);

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(1, result.SecretsPurged);
        Assert.Null(await BinnedSecretRowAsync(expired.Id));
        Assert.NotNull(await BinnedSecretRowAsync(fresh.Id));
    }

    [Fact]
    public async Task Purge_RestoredThenDeletedAgain_UsesNewDeletionTime()
    {
        var secret = await BinnedSecretAsync();
        var binRow = VaultKeys.SecretRowKey(secret.Id, recycled: true);
        var firstDeletion = DateTimeOffset.UtcNow - VaultLimits.RecycleBinRetention - TimeSpan.FromDays(10);
        await VaultRows.SetDeletedAtAsync(VaultsTable, Caller.ObjectId, binRow, firstDeletion, Token);
        var restored = Require(await Api.RestoreRecycledSecretAsync(secret.Id));
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(restored.Id, restored.ConcurrencyToken)).StatusCode);
        using var host = TimedHost();

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        var row = await BinnedSecretRowAsync(secret.Id);
        Assert.NotNull(row);
        Assert.True(row.GetDateTimeOffset("DeletedAt") > firstDeletion + VaultLimits.RecycleBinRetention);
        Assert.Equal(0, result.SecretsPurged);
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));
        var later = await PurgerOf(host).PurgeExpiredAsync(Token);
        Assert.Equal(1, later.SecretsPurged);
    }

    [Fact]
    public async Task RestoreAndPurgeRace_ExactlyOneWinsAndNothingIsHalfApplied()
    {
        const int Iterations = 10;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            var secret = await NewSecretAsync();
            secret = await ReplaceValueAsync(secret, "second");
            Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);

            var restore = Task.Run(() => Api.RestoreRecycledSecretAsync(secret.Id), Token);
            var purge = Task.Run(() => Api.PurgeRecycledSecretAsync(secret.Id), Token);
            await Task.WhenAll(restore, purge);

            var restored = (await restore).Response.StatusCode == System.Net.HttpStatusCode.OK;
            var purged = (await purge).StatusCode == System.Net.HttpStatusCode.NoContent;
            Assert.True(restored ^ purged, $"Iteration {iteration}: restored={restored} purged={purged}");
            var rows = (await VaultRowsAsync()).Where(row => row.RowKey.Contains(secret.Id.ToString("N"), StringComparison.Ordinal)).Select(row => row.RowKey).ToList();
            if (restored)
            {
                Assert.Contains($"s_{secret.Id:N}", rows);
                Assert.DoesNotContain($"bs_{secret.Id:N}", rows);
                Assert.Contains(rows, key => key.StartsWith("h_", StringComparison.Ordinal));
            }
            else
            {
                Assert.Empty(rows);
            }
        }
    }

    [Fact]
    public async Task Purge_ItemRestoredDuringRun_IsKept()
    {
        var restoredSecret = await BinnedSecretAsync();
        var kept = new List<Guid>();
        var racing = new PurgeRacingVaultStore(new TableVaultStore(new TableServiceClient(Factory.AzuriteConnectionString), Microsoft.Extensions.Logging.Abstractions.NullLogger<TableVaultStore>.Instance));
        racing.AfterNextRecycledBeforeRead(async (batch, _) =>
        {
            kept.AddRange(batch.Secrets.Select(secret => secret.Value.Id));
            Require(await Api.RestoreRecycledSecretAsync(restoredSecret.Id));
        });
        using var host = TimedHost(services =>
        {
            services.RemoveAll<IVaultStore>();
            services.AddSingleton<IVaultStore>(racing);
        });
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal([restoredSecret.Id], kept);
        Assert.Equal(0, result.SecretsPurged);
        Assert.Equal(1, result.Conflicts);
        var active = Require(await Api.GetSecretAsync(restoredSecret.Id));
        Assert.Equal(restoredSecret.Name, active.Name);
        Assert.Equal(restoredSecret.Id, Assert.Single(await WholeListAsync()).Id);
    }

    [Fact]
    public async Task Purge_Secret_DeletesBinRowAndEveryVersionRow()
    {
        var secret = await NewSecretAsync();
        secret = await ReplaceValueAsync(secret, "second");
        secret = await ReplaceValueAsync(secret, "third");
        secret = await ReplaceValueAsync(secret, "fourth");
        Assert.Equal(3, Require(await Api.ListHistoryAsync(secret.Id)).Items.Count);
        Assert.Equal(System.Net.HttpStatusCode.NoContent, (await Api.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken)).StatusCode);
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(1, result.SecretsPurged);
        var rows = await VaultRowsAsync();
        Assert.DoesNotContain(rows, row => row.RowKey.Contains(secret.Id.ToString("N"), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Purge_ExpiredFolderTree_RemovesEveryFolderAndSecret()
    {
        var folder = await NewFolderAsync("Top");
        var child = await NewFolderAsync("Child", folder.Id);
        var grandchild = await NewFolderAsync("Grandchild", child.Id);
        await NewSecretAsync(folder.Id);
        await NewSecretAsync(grandchild.Id);
        await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(2, result.SecretsPurged);
        Assert.Equal(3, result.FoldersPurged);
        Assert.Equal(0, result.Conflicts);
        Assert.Empty(await WholeBinAsync());
        Assert.DoesNotContain(await VaultRowsAsync(), row => row.RowKey != VaultKeys.KeyRingRowKey);
    }

    [Fact]
    public async Task Purge_Run_QueriesAllHavePartitionKey()
    {
        await BinnedSecretAsync();
        await AddUsersAsync(3);
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));
        StorageRequests.Clear();

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(1, result.SecretsPurged);
        Assert.Contains(StorageRequests.Requests, request => request.IsEntityQuery);
        AssertNoQueryWithoutPartitionKey();
    }

    [Fact]
    public async Task Purge_WritesSecretPurgedWithSystemActor()
    {
        var secret = await BinnedSecretAsync();
        var folder = await NewFolderAsync("Binned");
        await Api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        await PurgerOf(host).PurgeExpiredAsync(Token);

        var secretEvent = Assert.Single(await AuditEventsAsync(AuditEventType.SecretPurged));
        Assert.Equal(AuditActors.System.ToString(GuidFormat), secretEvent.GetString("ActorId"));
        Assert.Equal(Caller.ObjectId.ToString(GuidFormat), secretEvent.GetString("OwnerId"));
        Assert.Equal(secret.Id.ToString(GuidFormat), secretEvent.GetString("TargetId"));
        Assert.Equal(secret.Id.ToString(GuidFormat), secretEvent.GetString("SecretId"));
        Assert.Null(secretEvent.GetString("Reason"));
        var folderEvent = Assert.Single(await AuditEventsAsync(AuditEventType.FolderPurged));
        Assert.Equal(AuditActors.System.ToString(GuidFormat), folderEvent.GetString("ActorId"));
        Assert.Equal(folder.Id.ToString(GuidFormat), folderEvent.GetString("TargetId"));
        Assert.Null(folderEvent.GetString("SecretId"));
        var index = await SecretAuditRowsAsync(Caller.ObjectId, secret.Id);
        Assert.Equal(nameof(AuditEventType.SecretPurged), index[0].GetString("EventType"));
    }

    [Fact]
    public async Task Purge_SkipsUsersWithEmptyBins_WithOneQueryEach()
    {
        await AddUsersAsync(3);
        using var host = TimedHost();
        StorageRequests.Clear();

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(3, result.UsersScanned);
        Assert.Equal(0, result.SecretsPurged);
        var vaultQueries = StorageRequests.VaultReads();
        Assert.Equal(3, vaultQueries.Count);
        Assert.All(vaultQueries, request => Assert.True(request.IsEntityQuery));
        Assert.Empty(StorageRequests.VaultWrites());
    }

    [Fact]
    public async Task Purge_RunTwice_SecondRunPurgesNothing()
    {
        await BinnedSecretAsync();
        await BinnedSecretAsync();
        using var host = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var first = await PurgerOf(host).PurgeExpiredAsync(Token);
        var second = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.Equal(2, first.SecretsPurged);
        Assert.False(second.Skipped);
        Assert.Equal(0, second.SecretsPurged);
        Assert.Equal(0, second.FoldersPurged);
        Assert.Equal(0, second.Conflicts);
        Assert.Equal(2, (await AuditEventsAsync(AuditEventType.SecretPurged)).Count);
    }

    [Fact]
    public async Task Purge_LeaseHeldByOtherInstance_Skips()
    {
        var secret = await BinnedSecretAsync();
        using var host = TimedHost();
        var leases = host.Services.GetRequiredService<IMaintenanceLeaseStore>();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));
        Assert.NotNull(await leases.TryAcquireAsync(RecycleBinPurger.LeaseName, "other-instance", TimeSpan.FromHours(1), Token));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.True(result.Skipped);
        Assert.Equal(0, result.SecretsPurged);
        Assert.NotNull(await BinnedSecretRowAsync(secret.Id));
        Assert.Empty(await AuditEventsAsync(AuditEventType.SecretPurged));
    }

    [Fact]
    public async Task Purge_ExpiredLeaseOfCrashedInstance_IsTakenOver()
    {
        var secret = await BinnedSecretAsync();
        using var host = TimedHost();
        var leases = host.Services.GetRequiredService<IMaintenanceLeaseStore>();
        var crashed = await leases.TryAcquireAsync(RecycleBinPurger.LeaseName, "crashed-instance", TimeSpan.FromMinutes(1), Token);
        Assert.NotNull(crashed);
        crashed.Value.RunId = Guid.NewGuid();
        crashed.Value.RunStartedAt = _time.GetUtcNow();
        Assert.NotNull(await leases.RenewAsync(crashed.Value, crashed.ETag, TimeSpan.FromMinutes(1), Token));
        _time.Advance(JustOverRetention + TimeSpan.FromMinutes(2));

        var result = await PurgerOf(host).PurgeExpiredAsync(Token);

        Assert.False(result.Skipped);
        Assert.Equal(1, result.SecretsPurged);
        Assert.Null(await BinnedSecretRowAsync(secret.Id));
        var lease = await GetTableClient(TableNames.Maintenance).GetEntityAsync<TableEntity>(MaintenanceKeys.PartitionKey, RecycleBinPurger.LeaseName, cancellationToken: Token);
        Assert.Null(lease.Value.GetString("RunId"));
        Assert.NotEqual("crashed-instance", lease.Value.GetString("Holder"));
        Assert.NotNull(lease.Value.GetDateTimeOffset("LastCompletedAt"));
    }

    [Fact]
    public async Task Purge_CancelledAfterFirstUserPage_ResumesFromCheckpoint()
    {
        await AddUsersAsync(UserPageSize + 4);
        var secret = await BinnedSecretAsync();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var host = TimedHost(services =>
        {
            services.RemoveAll<IUserStore>();
            services.AddSingleton<IUserStore>(provider => new CancellingUserStore(
                ActivatorUtilities.CreateInstance<TableUserStore>(provider),
                cancellation));
        });
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));
        var purger = PurgerOf(host);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => purger.PurgeExpiredAsync(cancellation.Token));

        var lease = await GetTableClient(TableNames.Maintenance).GetEntityAsync<TableEntity>(MaintenanceKeys.PartitionKey, RecycleBinPurger.LeaseName, cancellationToken: Token);
        Assert.NotNull(lease.Value.GetString("RunId"));
        var checkpoint = Guid.Parse(lease.Value.GetString("AfterUserId")!);
        Assert.NotNull(await BinnedSecretRowAsync(secret.Id));
        var resumed = await purger.PurgeExpiredAsync(Token);
        Assert.False(resumed.Skipped);
        Assert.Equal(5, resumed.UsersScanned);
        Assert.Equal(new Guid(UserPageSize, 0, 0, [0, 0, 0, 0, 0, 0, 0, 0]), checkpoint);
        Assert.Equal(1, resumed.SecretsPurged);
        Assert.Null(await BinnedSecretRowAsync(secret.Id));
        var finished = await GetTableClient(TableNames.Maintenance).GetEntityAsync<TableEntity>(MaintenanceKeys.PartitionKey, RecycleBinPurger.LeaseName, cancellationToken: Token);
        Assert.Null(finished.Value.GetString("RunId"));
        Assert.Null(finished.Value.GetString("AfterUserId"));
    }

    [Fact]
    public async Task Purge_TwoInstancesInParallel_EachItemPurgedOnceAndAuditedOnce()
    {
        var secrets = new List<SecretDto>();
        for (var index = 0; index < 6; index++)
        {
            secrets.Add(await BinnedSecretAsync());
        }

        using var first = TimedHost();
        using var second = TimedHost();
        _time.Advance(JustOverRetention + TimeSpan.FromSeconds(1));

        var results = await Task.WhenAll(
            Task.Run(() => PurgerOf(first).PurgeExpiredAsync(Token), Token),
            Task.Run(() => PurgerOf(second).PurgeExpiredAsync(Token), Token));

        Assert.Equal(6, results.Sum(result => result.SecretsPurged));
        Assert.Empty(await WholeBinAsync());
        var audited = await AuditEventsAsync(AuditEventType.SecretPurged);
        Assert.Equal(6, audited.Count);
        Assert.Equal(secrets.Select(secret => secret.Id.ToString(GuidFormat)).Order(), audited.Select(row => row.GetString("TargetId")).Order());
    }

    private sealed class CancellingUserStore(IUserStore inner, CancellationTokenSource cancellation) : IUserStore
    {
        private int _listCalls;

        public Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken) => inner.GetAsync(userId, cancellationToken);

        public Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken) => inner.AddAsync(user, cancellationToken);

        public Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken) => inner.ReplaceAsync(user, etag, cancellationToken);

        public async Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _listCalls) == 2)
            {
                await cancellation.CancelAsync();
            }

            return await inner.ListAsync(pageSize, afterUserId, cancellationToken);
        }

        public Task<IReadOnlyList<User>> ListAdminsAsync(int limit, CancellationToken cancellationToken) => inner.ListAdminsAsync(limit, cancellationToken);
    }
}
