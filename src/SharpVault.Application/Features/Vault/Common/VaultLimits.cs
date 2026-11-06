// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Features.Vault.Common;

public static class VaultLimits
{
    public const int NameMaxLength = 200;
    public const int UsernameMaxLength = 256;
    public const int UrlMaxLength = 2048;
    public const int ValueMaxLength = 8192;
    public const int NotesMaxLength = 8192;
    public const int TagMaxLength = 50;
    public const int MaxTags = 20;
    public const int FolderNameMaxLength = 100;
    public const int MaxFolderDepth = 10;
    public const int MaxFoldersPerVault = 1000;
    public const int MaxItemsPerFolderOperation = 1000;
    public const int HistoryVersionLimit = 20;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int SearchMaxLength = 100;
    public const int SearchScanBatchSize = 1000;
    public const int SearchMaxScannedRows = 5000;
    public const int AuditParallelism = 8;
    public const int FolderQueryParallelism = 4;
    public const int ETagMaxLength = 100;
    public const int CurrentMetadataKeyVersion = 1;

    public static readonly TimeSpan RecycleBinRetention = TimeSpan.FromDays(30);

    public static readonly TimeSpan CommittedAuditTimeout = TimeSpan.FromSeconds(30);
}
