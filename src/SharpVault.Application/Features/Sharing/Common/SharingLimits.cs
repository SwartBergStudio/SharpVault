// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Sharing;

namespace SharpVault.Application.Features.Sharing.Common;

public static class SharingLimits
{
    public const int MaxGrantsPerItem = 100;
    public const int GrantReadLimit = MaxGrantsPerItem * 2;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 100;
    public const int DirectorySearchLimit = 10;
    public const int MaxDirectoryRowsScanned = 1000;
    public const int ItemReadParallelism = 16;
    public const int DirectoryStatusParallelism = 4;
    public const int SearchMinLength = 3;
    public const int SearchMaxLength = 100;
    public const int MaxEmptyInboxPagesPerCall = 20;
    public const int RevokeAttempts = 3;
    public const SharePermissionDto ManagerMaxGrant = SharePermissionDto.Edit;

    public static readonly TimeSpan InboxCleanupTimeout = TimeSpan.FromSeconds(30);
}
