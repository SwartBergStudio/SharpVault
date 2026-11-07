// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.OneTimeLinks;

public static class OneTimeLinkPayloadColumns
{
    public const int ColumnMaxBytes = 65536;

    public const int MaxColumns = 2;

    public static (byte[] First, byte[]? Second) Split(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Length > ColumnMaxBytes * MaxColumns)
        {
            throw new ArgumentException(OneTimeLinkStorageMessages.PayloadTooLarge, nameof(payload));
        }

        return payload.Length <= ColumnMaxBytes
            ? (payload, null)
            : (payload[..ColumnMaxBytes], payload[ColumnMaxBytes..]);
    }

    public static byte[] Join(byte[]? first, byte[]? second)
    {
        if (first is null)
        {
            throw new InvalidDataException(OneTimeLinkStorageMessages.InvalidPayloadColumns);
        }

        return second is null ? first : [.. first, .. second];
    }
}
