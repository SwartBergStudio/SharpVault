// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Maintenance;
using SharpVault.Domain.OneTimeLinks;

namespace SharpVault.Application.Features.OneTimeLinks.Common;

public sealed partial class OneTimeLinkPurger(
    IOneTimeLinkStore links,
    IMaintenanceLeaseStore leases,
    TimeProvider timeProvider,
    IOptions<OneTimeLinkOptions> options,
    ILogger<OneTimeLinkPurger> logger) : IOneTimeLinkPurger
{
    public const string LeaseName = "one-time-link-purge";

    private const int BucketPageSize = 100;
    private static readonly TimeSpan CheckpointSlack = TimeSpan.FromHours(1);

    private readonly string _holderId = Guid.NewGuid().ToString("D");

    public async Task PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var leaseDuration = options.Value.PurgeLeaseDuration;
        var acquired = await leases.TryAcquireAsync(LeaseName, _holderId, leaseDuration, cancellationToken);
        if (acquired is null)
        {
            return;
        }

        var lease = acquired.Value;
        var etag = acquired.ETag;
        var now = timeProvider.GetUtcNow();
        var currentHour = FloorToHour(now);
        var deleted = 0;

        for (var hour = StartHour(lease.LastCompletedAt, now); hour <= currentHour; hour = hour.AddHours(1))
        {
            deleted += await PurgeHourAsync(hour, now, cancellationToken);

            var renewed = await leases.RenewAsync(lease, etag, leaseDuration, cancellationToken);
            if (renewed is null)
            {
                LogLeaseLost(logger);
                return;
            }

            etag = renewed.ETag;
        }

        lease.LastCompletedAt = now;
        await leases.ReleaseAsync(lease, etag, cancellationToken);
        LogFinished(logger, deleted);
    }

    private async Task<int> PurgeHourAsync(DateTimeOffset hour, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var deleted = 0;
        string? continuationToken = null;
        do
        {
            var page = await links.ListExpiryBucketAsync(hour, BucketPageSize, continuationToken, cancellationToken);
            foreach (var entry in page.Items)
            {
                if (entry.ExpiresAt > now)
                {
                    continue;
                }

                await links.DeleteExpiredAsync(entry, cancellationToken);
                deleted++;
            }

            continuationToken = page.ContinuationToken;
        }
        while (continuationToken is not null);

        return deleted;
    }

    private static DateTimeOffset StartHour(DateTimeOffset? lastCompletedAt, DateTimeOffset now) =>
        FloorToHour(lastCompletedAt ?? now - OneTimeLinkLimits.AbsoluteMaxLifetime - CheckpointSlack);

    private static DateTimeOffset FloorToHour(DateTimeOffset value) =>
        new(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerHour), TimeSpan.Zero);

    [LoggerMessage(Level = LogLevel.Warning, Message = "One-time link purge lost its lease before it finished and will resume next run")]
    private static partial void LogLeaseLost(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "One-time link purge finished: {LinksPurged} expired links removed")]
    private static partial void LogFinished(ILogger logger, int linksPurged);
}
