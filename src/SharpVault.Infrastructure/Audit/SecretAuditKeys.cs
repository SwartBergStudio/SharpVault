// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;

namespace SharpVault.Infrastructure.Audit;

public static class SecretAuditKeys
{
    public static string PartitionKey(Guid ownerId) => ownerId.ToString("D", CultureInfo.InvariantCulture);

    public static string RowKey(Guid secretId, DateTimeOffset occurredAt, Guid id) =>
        secretId.ToString("N", CultureInfo.InvariantCulture)
        + "_"
        + (DateTime.MaxValue.Ticks - occurredAt.UtcTicks).ToString("D19", CultureInfo.InvariantCulture)
        + "_"
        + id.ToString("N", CultureInfo.InvariantCulture);
}
