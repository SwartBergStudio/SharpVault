// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed partial class TransferNoticeBanner
{
    public const string DismissFailedText = "The notice could not be dismissed. Try again.";

    private readonly List<TransferNoticeDto> _rows = [];
    private readonly StatusMessage _status = new();

    protected override async Task<ApiResponse<TransferNoticeListDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await Notices.ListAsync(cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Items);
        }

        return response;
    }

    protected override bool IsEmpty(TransferNoticeListDto value) => value.Items.Count == 0;

    public static string NoticeText(TransferNoticeDto notice) => notice.Kind switch
    {
        TransferNoticeKindDto.Received =>
            $"{notice.SourceDisplayName}'s items were moved to you on {TransferStatusText.Stamp(notice.CompletedAt)}. {notice.ItemCount} items moved.",
        TransferNoticeKindDto.RequestCompleted =>
            $"The transfer of {notice.SourceDisplayName}'s items that you requested is complete. {notice.ItemCount} items moved.",
        TransferNoticeKindDto.ApprovalCompleted =>
            $"The transfer of {notice.SourceDisplayName}'s items that you approved is complete. {notice.ItemCount} items moved.",
        _ => throw new ArgumentOutOfRangeException(nameof(notice), notice.Kind, "Unknown notice kind."),
    };

    public static string OpenHref(TransferNoticeDto notice) => notice.Kind switch
    {
        TransferNoticeKindDto.Received => notice.FolderId is { } folderId ? VaultRoutes.Folder(folderId) : VaultRoutes.Vault,
        _ => TransferRoutes.Transfer(notice.TransferId),
    };

    private Task DismissAsync(TransferNoticeDto notice) => RunApiCommandAsync(
        token => Notices.DismissAsync(notice.TransferId, token),
        response => ApplyDismissAsync(response, notice.TransferId),
        () => _status.Show(DismissFailedText, AlertKind.Danger));

    private Task ApplyDismissAsync(ApiResponse<bool> response, Guid transferId)
    {
        if (response.Status is ApiCallStatus.Success or ApiCallStatus.NotFound)
        {
            _rows.RemoveAll(notice => notice.TransferId == transferId);
            _status.Clear();
            return Task.CompletedTask;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        return Task.CompletedTask;
    }
}
