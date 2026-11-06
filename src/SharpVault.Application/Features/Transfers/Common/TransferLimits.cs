// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;

namespace SharpVault.Application.Features.Transfers.Common;

public static class TransferLimits
{
    public const int ReasonMinLength = ReasonRules.MinLength;

    public const int ReasonMaxLength = ReasonRules.MaxLength;

    public const int MaxSelectedFolders = 100;

    public const int FolderLimit = VaultLimits.MaxFoldersPerVault;

    public const int InventoryMaxPages = 50;

    public const int ReencryptParallelism = 8;

    public const int MaxStagePasses = 5;

    public const int MaxReleasePasses = 20;

    public const int MaxPublishPasses = 5;

    public const int MaxTransactionActions = 100;

    public const int NoticeListLimit = 20;

    public const int ReadPageSize = 100;

    public const int PublishPageSize = 50;

    public const int GrantPageSize = 200;

    public const int InventoryPageSize = 1000;

    public static readonly TimeSpan BlockedRetryDelay = TimeSpan.FromSeconds(30);
}
