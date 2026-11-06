// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Navigation;

public static class TransferRoutes
{
    public const string Transfers = "/admin/transfers";
    public const string NewTransfer = "/admin/transfers/new";
    public const string Shares = "/transfers/shares";
    public const string SourceUserParameter = "sourceUserId";

    public static string Transfer(Guid transferId) => $"{Transfers}/{transferId:D}";

    public static string NewTransferFrom(Guid sourceUserId) => $"{NewTransfer}?{SourceUserParameter}={sourceUserId:D}";
}
