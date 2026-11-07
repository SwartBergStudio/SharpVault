// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.RegularExpressions;
using SharpVault.Infrastructure.Vaults;

namespace SharpVault.Infrastructure.OneTimeLinks;

public static partial class OneTimeLinkKeys
{
    public const string LinkPartitionPrefix = "l_";
    public const string LinkRowKey = "link";
    public const string CreatorPartitionPrefix = "c_";
    public const string IndexPrefix = "a_";
    public const string NoticePrefix = "n_";
    public const string ExpiryPartitionPrefix = "e_";

    private const string GuidFormat = "N";
    private const string GuidFormatD = "D";
    private const string TicksFormat = "D19";
    private const string ExpiryHourFormat = "yyyyMMddHH";
    private const string NoticePattern = @"^n_[0-9]{19}_[0-9a-f]{32}\z";

    public static string IndexUpperBound { get; } = VaultKeys.UpperBound(IndexPrefix);

    public static string NoticeUpperBound { get; } = VaultKeys.UpperBound(NoticePrefix);

    public static string LinkPartition(Guid linkId) => LinkPartitionPrefix + Format(linkId);

    public static string CreatorPartition(Guid creatorId) => CreatorPartitionPrefix + creatorId.ToString(GuidFormatD, CultureInfo.InvariantCulture);

    public static string IndexRowKey(Guid linkId) => IndexPrefix + Format(linkId);

    public static string NoticeRowKey(DateTimeOffset openedAt, Guid linkId) =>
        NoticePrefix
        + (DateTime.MaxValue.Ticks - openedAt.UtcTicks).ToString(TicksFormat, CultureInfo.InvariantCulture)
        + "_"
        + Format(linkId);

    public static string ExpiryPartition(DateTimeOffset expiresAt) =>
        ExpiryPartitionPrefix + expiresAt.UtcDateTime.ToString(ExpiryHourFormat, CultureInfo.InvariantCulture);

    public static string ExpiryRowKey(Guid linkId) => Format(linkId);

    public static bool IsNoticeRowKey(string rowKey) => NoticeRowKeyRegex().IsMatch(rowKey);

    public static Guid ParseLinkId(string text) => Guid.ParseExact(text, GuidFormat);

    public static Guid ParseId(string text) => Guid.ParseExact(text, GuidFormatD);

    public static string Format(Guid id) => id.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string FormatId(Guid id) => id.ToString(GuidFormatD, CultureInfo.InvariantCulture);

    [GeneratedRegex(NoticePattern, RegexOptions.CultureInvariant)]
    private static partial Regex NoticeRowKeyRegex();
}
