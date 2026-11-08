// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.OneTimeLinks;

public sealed partial class OneTimeLinkNoticeBanner
{
    public const string DismissFailedText = "The notice could not be dismissed. Try again.";

    private readonly List<OneTimeLinkNoticeDto> _rows = [];
    private readonly StatusMessage _status = new();

    protected override async Task<ApiResponse<OneTimeLinkNoticeListDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await Links.ListNoticesAsync(cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Items);
        }

        return response;
    }

    protected override bool IsEmpty(OneTimeLinkNoticeListDto value) => value.Items.Count == 0;

    public static string NoticeText(OneTimeLinkNoticeDto notice) =>
        $"Your one-time link for \"{OneTimeLinkText.NameOf(notice.ItemName)}\" was opened on {StampText.For(notice.OpenedAt)}.";

    private Task DismissAsync(OneTimeLinkNoticeDto notice) => RunApiCommandAsync(
        token => Links.DismissNoticeAsync(notice.NoticeId, token),
        response => ApplyDismissAsync(response, notice.NoticeId),
        () => _status.Show(DismissFailedText, AlertKind.Danger));

    private Task ApplyDismissAsync(ApiResponse<bool> response, string noticeId)
    {
        if (response.Status is ApiCallStatus.Success or ApiCallStatus.NotFound)
        {
            _rows.RemoveAll(notice => notice.NoticeId == noticeId);
            _status.Clear();
            return Task.CompletedTask;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        return Task.CompletedTask;
    }
}
