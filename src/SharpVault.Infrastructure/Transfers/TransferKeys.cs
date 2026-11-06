// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.Transfers;

public static class TransferKeys
{
    public const string TransferPartition = "transfer";
    public const string JobPrefix = "job_";
    public const string MarkerPrefix = "src_";
    public const string NoticePartitionPrefix = "notice_";
    public const string RootSourceETag = "root";

    private const string GuidFormat = "N";
    private const int GuidTextLength = 32;
    private const string HexDigits = "0123456789abcdef";
    private const int MaxHexValue = 15;

    public static string JobRowKey(Guid transferId) => JobPrefix + Complement(transferId);

    public static string JobRangeUpperBound { get; } = VaultKeys.UpperBound(JobPrefix);

    public static string MarkerRangeUpperBound { get; } = VaultKeys.UpperBound(MarkerPrefix);

    public static Guid? JobIdFromRowKey(string rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);

        if (!rowKey.StartsWith(JobPrefix, StringComparison.Ordinal) || rowKey.Length != JobPrefix.Length + GuidTextLength)
        {
            return null;
        }

        return TryParseComplement(rowKey[JobPrefix.Length..]);
    }

    public static string MarkerRowKey(Guid sourceUserId) =>
        MarkerPrefix + sourceUserId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string NoticePartition(Guid userId) =>
        NoticePartitionPrefix + userId.ToString("D", CultureInfo.InvariantCulture);

    public static string NoticeRowKey(Guid transferId) => Complement(transferId);

    public static Guid? NoticeTransferIdFromRowKey(string rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);

        return rowKey.Length == GuidTextLength ? TryParseComplement(rowKey) : null;
    }

    private static Guid? TryParseComplement(string text)
    {
        var restored = Complement(text);
        return Guid.TryParseExact(restored, GuidFormat, out var id) ? id : null;
    }

    private static string Complement(Guid id) => Complement(id.ToString(GuidFormat, CultureInfo.InvariantCulture));

    private static string Complement(string text) =>
        string.Create(text.Length, text, static (span, source) =>
        {
            for (var index = 0; index < source.Length; index++)
            {
                var value = HexValue(source[index]);
                span[index] = value < 0 ? source[index] : HexDigits[MaxHexValue - value];
            }
        });

    private static int HexValue(char digit) => digit switch
    {
        >= '0' and <= '9' => digit - '0',
        >= 'a' and <= 'f' => digit - 'a' + 10,
        _ => -1,
    };
}
