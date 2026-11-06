// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Domain.Sharing;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Sharing;

internal static class ShareInboxKeys
{
    private const string GuidFormat = "N";

    public static string PartitionKey(Guid recipientId) => recipientId.ToString("D", CultureInfo.InvariantCulture);

    public static string RowKey(Guid ownerId, ShareItemKind kind, Guid itemId) =>
        ownerId.ToString(GuidFormat, CultureInfo.InvariantCulture)
        + "_" + VaultKeys.KindCode(kind)
        + "_" + itemId.ToString(GuidFormat, CultureInfo.InvariantCulture);
}
