// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;

namespace SharpVault.Infrastructure.Audit;

public static class AuditKeys
{
    public static string PartitionKey(DateTimeOffset occurredAt) =>
        occurredAt.UtcDateTime.ToString("yyyyMM", CultureInfo.InvariantCulture);

    public static string RowKey(DateTimeOffset occurredAt, Guid id) =>
        (DateTime.MaxValue.Ticks - occurredAt.UtcTicks).ToString("D19", CultureInfo.InvariantCulture)
        + "_"
        + id.ToString("N");
}
