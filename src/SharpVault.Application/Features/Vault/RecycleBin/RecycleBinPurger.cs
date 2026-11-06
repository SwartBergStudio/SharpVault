// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Audit;

namespace SharpVault.Application.Features.Vault.RecycleBin;

public sealed partial class RecycleBinPurger(
    IUserStore users,
    IVaultStore vaults,
    IMaintenanceLeaseStore leases,
    RecycledItemEraser eraser,
    IAuditLog auditLog,
    TimeProvider timeProvider,
    IOptions<RecycleBinPurgerOptions> options,
    ILogger<RecycleBinPurger> logger) : IRecycleBinPurger
{
    public const string LeaseName = "recycle-bin-purge";

    private const int UserPageSize = 100;
    private const string TargetIdFormat = "D";

    private readonly string _holderId = Guid.NewGuid().ToString(TargetIdFormat);

    public async Task<RecycleBinPurgeResult> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var leaseDuration = options.Value.LeaseDuration;
        var acquired = await leases.TryAcquireAsync(LeaseName, _holderId, leaseDuration, cancellationToken);
        if (acquired is null)
        {
            LogSkipped(logger);
            return RecycleBinPurgeResult.NotRun;
        }

        var lease = acquired.Value;
        var etag = acquired.ETag;
        if (lease.RunId is null)
        {
            lease.RunId = Guid.NewGuid();
            lease.AfterUserId = null;
            lease.RunStartedAt = timeProvider.GetUtcNow();
            var started = await leases.RenewAsync(lease, etag, leaseDuration, cancellationToken);
            if (started is null)
            {
                return RecycleBinPurgeResult.NotRun;
            }

            etag = started.ETag;
        }

        var tally = new Tally();
        var cutoff = timeProvider.GetUtcNow() - VaultLimits.RecycleBinRetention;
        var after = lease.AfterUserId;

        while (true)
        {
            var page = await users.ListAsync(UserPageSize, after, cancellationToken);
            foreach (var user in page.Users)
            {
                tally.UsersScanned++;
                await PurgeUserAsync(user.Id, cutoff, tally, cancellationToken);
            }

            if (page.NextAfterUserId is null)
            {
                break;
            }

            after = page.NextAfterUserId;
            lease.AfterUserId = after;
            var renewed = await leases.RenewAsync(lease, etag, leaseDuration, cancellationToken);
            if (renewed is null)
            {
                return tally.ToResult();
            }

            etag = renewed.ETag;
        }

        lease.RunId = null;
        lease.AfterUserId = null;
        lease.LastCompletedAt = timeProvider.GetUtcNow();
        await leases.ReleaseAsync(lease, etag, cancellationToken);

        var result = tally.ToResult();
        LogFinished(logger, result.UsersScanned, result.SecretsPurged, result.FoldersPurged, result.Conflicts);
        return result;
    }

    private async Task PurgeUserAsync(Guid owner, DateTimeOffset cutoff, Tally tally, CancellationToken cancellationToken)
    {
        var expired = await vaults.ListRecycledBeforeAsync(owner, cutoff, cancellationToken);

        foreach (var secret in expired.Secrets)
        {
            var erased = await eraser.EraseSecretAsync(owner, secret, cancellationToken);
            await AuditGrantsAsync(erased, tally);
            erased.Write.Interruption?.Throw();
            if (erased.Write.Result != StoreWriteResult.Written)
            {
                tally.Conflicts++;
                continue;
            }

            tally.SecretsPurged++;
            await AppendAsync(new VaultAuditItem(AuditEventType.SecretPurged, owner, secret.Value.Id, secret.Value.Id));
        }

        foreach (var folder in expired.Folders)
        {
            var erased = await eraser.EraseFoldersAsync(owner, [folder], cancellationToken);
            await AuditGrantsAsync(erased, tally);
            erased.Write.Interruption?.Throw();
            if (erased.Write.Result != StoreWriteResult.Written)
            {
                tally.Conflicts++;
                continue;
            }

            tally.FoldersPurged++;
            await AppendAsync(new VaultAuditItem(AuditEventType.FolderPurged, owner, folder.Value.Id, null));
        }
    }

    private async Task AuditGrantsAsync(ErasedItems erased, Tally tally)
    {
        foreach (var item in erased.CommittedGrantAuditItems())
        {
            await AppendAsync(item);
            tally.GrantsErased++;
        }
    }

    private async Task AppendAsync(VaultAuditItem item)
    {
        var eventType = item.EventType;
        var owner = item.OwnerId;
        var targetId = item.TargetId;
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            ActorId = AuditActors.System,
            OwnerId = owner,
            TargetId = targetId.ToString(TargetIdFormat),
            SecretId = item.SecretId,
            RecipientId = item.RecipientId,
            SharePermission = item.SharePermission,
            OccurredAt = timeProvider.GetUtcNow(),
            IpAddress = null,
            Reason = null,
        };

        using var timeout = new CancellationTokenSource(VaultLimits.CommittedAuditTimeout, timeProvider);
        try
        {
            await auditLog.AppendAsync(entry, timeout.Token);
        }
        catch (Exception)
        {
            LogAuditFailedAfterErase(logger, eventType, owner, targetId);
            throw;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recycle bin purge finished: {UsersScanned} users, {SecretsPurged} secrets, {FoldersPurged} folders, {Conflicts} conflicts")]
    private static partial void LogFinished(ILogger logger, int usersScanned, int secretsPurged, int foldersPurged, int conflicts);

    [LoggerMessage(Level = LogLevel.Error, Message = "Audit write failed after erasing {EventType} for owner {OwnerId} target {TargetId}")]
    private static partial void LogAuditFailedAfterErase(ILogger logger, AuditEventType eventType, Guid ownerId, Guid targetId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recycle bin purge skipped because another instance holds the lease")]
    private static partial void LogSkipped(ILogger logger);

    private sealed class Tally
    {
        public int UsersScanned { get; set; }

        public int SecretsPurged { get; set; }

        public int FoldersPurged { get; set; }

        public int Conflicts { get; set; }

        public int GrantsErased { get; set; }

        public RecycleBinPurgeResult ToResult() => new(false, UsersScanned, SecretsPurged, FoldersPurged, Conflicts, GrantsErased);
    }
}
