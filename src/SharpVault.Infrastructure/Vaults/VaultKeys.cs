// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using SharpVault.Domain.Sharing;

namespace SharpVault.Infrastructure.Vaults;

public static class VaultKeys
{
    public const string KeyRingRowKey = "vault";
    public const string SecretPrefix = "s_";
    public const string RecycledSecretPrefix = "bs_";
    public const string FolderPrefix = "f_";
    public const string RecycledFolderPrefix = "bf_";
    public const string VersionPrefix = "h_";
    public const string RecycleBinLowerBound = "b";
    public const string RecycleBinUpperBound = "c";
    public const string RootFolderKey = "root";
    public const string GrantByItemPrefix = "gi_";
    public const string GrantByRecipientPrefix = "gr_";
    public const string SecretKindCode = "s";
    public const string FolderKindCode = "f";
    public const string StagingPrefix = "x_";
    public const string InventoryLowerBound = "b";
    public const string InventoryUpperBound = "t";
    public const string GrantRangeLowerBound = "g";
    public const string GrantRangeUpperBound = "h";
    public const string StagedFolderCode = "f_";
    public const string StagedVersionCode = "h_";
    public const string StagedSkippedCode = "k_";
    public const string StagedSecretCode = "s_";
    public const string StagingFenceSuffix = "-fence";

    private const string GuidFormat = "N";
    private const int GuidTextLength = 32;

    public static string PartitionKey(Guid ownerId) => ownerId.ToString("D", CultureInfo.InvariantCulture);

    public static string SecretRowKey(Guid id, bool recycled) =>
        (recycled ? RecycledSecretPrefix : SecretPrefix) + id.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string FolderRowKey(Guid id, bool recycled) =>
        (recycled ? RecycledFolderPrefix : FolderPrefix) + id.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string VersionRowKey(Guid secretId, Guid versionId) =>
        VersionRangeStart(secretId) + versionId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string VersionRangeStart(Guid secretId) =>
        VersionPrefix + secretId.ToString(GuidFormat, CultureInfo.InvariantCulture) + "_";

    public static string KindCode(ShareItemKind kind) => kind switch
    {
        ShareItemKind.Secret => SecretKindCode,
        ShareItemKind.Folder => FolderKindCode,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static string GrantByItemRowKey(ShareItemKind kind, Guid itemId, Guid recipientId) =>
        GrantItemRangeStart(kind, itemId) + recipientId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string GrantByRecipientRowKey(Guid recipientId, ShareItemKind kind, Guid itemId) =>
        GrantRecipientRangeStart(recipientId) + KindCode(kind) + "_" + itemId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string GrantItemRangeStart(ShareItemKind kind, Guid itemId) =>
        GrantByItemPrefix + KindCode(kind) + "_" + itemId.ToString(GuidFormat, CultureInfo.InvariantCulture) + "_";

    public static string GrantRecipientRangeStart(Guid recipientId) =>
        GrantByRecipientPrefix + recipientId.ToString(GuidFormat, CultureInfo.InvariantCulture) + "_";

    public static string GrantRecipientFolderRangeStart(Guid recipientId) =>
        GrantRecipientRangeStart(recipientId) + FolderKindCode + "_";

    public static string StagingRangeStart(Guid transferId) =>
        StagingPrefix + transferId.ToString(GuidFormat, CultureInfo.InvariantCulture) + "_";

    public static string StagingFenceRowKey(Guid transferId) =>
        StagingPrefix + transferId.ToString(GuidFormat, CultureInfo.InvariantCulture) + StagingFenceSuffix;

    public static string StagedFolderRowKey(Guid transferId, Guid folderId) =>
        StagingRangeStart(transferId) + StagedFolderCode + folderId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string StagedSecretRowKey(Guid transferId, Guid secretId) =>
        StagingRangeStart(transferId) + StagedSecretCode + secretId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string StagedSkippedRowKey(Guid transferId, Guid secretId) =>
        StagingRangeStart(transferId) + StagedSkippedCode + secretId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string StagedVersionRowKey(Guid transferId, Guid secretId, Guid versionId) =>
        StagedVersionRangeStart(transferId, secretId) + versionId.ToString(GuidFormat, CultureInfo.InvariantCulture);

    public static string StagedVersionRangeStart(Guid transferId, Guid secretId) =>
        StagingRangeStart(transferId) + StagedVersionCode + secretId.ToString(GuidFormat, CultureInfo.InvariantCulture) + "_";

    public static string UpperBound(string prefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        return string.Concat(prefix.AsSpan(0, prefix.Length - 1), ((char)(prefix[^1] + 1)).ToString());
    }

    public static string FolderKey(Guid? folderId) =>
        folderId?.ToString(GuidFormat, CultureInfo.InvariantCulture) ?? RootFolderKey;

    public static Guid? ParseFolderKey(string? key)
    {
        if (key is null || key.Length != GuidTextLength)
        {
            return null;
        }

        return Guid.TryParseExact(key, GuidFormat, out var id) ? id : null;
    }
}
