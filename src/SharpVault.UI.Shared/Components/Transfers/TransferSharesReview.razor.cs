// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.AspNetCore.Components.Web;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed partial class TransferSharesReview
{
    public const int PageSize = 50;
    public const string UnavailableItemText = "Item no longer available";
    public const string GoneText = "That share was already removed. The list has been reloaded.";
    public const string LoadMoreFailedText = "More shares could not be loaded. Try again.";

    private const string GrantedFormat = "yyyy-MM-dd";
    private const string SecretKindText = "Secret";

    private readonly List<OwnedShareDto> _rows = [];
    private readonly StatusMessage _status = new();
    private string? _continuation;
    private Guid? _callerId;
    private OwnedShareDto? _revoking;

    protected override async Task<ApiResponse<OwnedSharePageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentUserAccessor.GetAsync(cancellationToken);
        if (current.Status != ApiCallStatus.Success || current.Value is null)
        {
            return new ApiResponse<OwnedSharePageDto>(current.Status, null);
        }

        _callerId = current.Value.UserId;
        var response = await Grants.ListOwnedSharesAsync(PageSize, null, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Items);
            _continuation = response.Value.ContinuationToken;
            LoadMoreFailed = false;
        }

        return response;
    }

    protected override bool IsEmpty(OwnedSharePageDto value) => value.Items.Count == 0 && value.ContinuationToken is null;

    protected override void OnAccessDenied()
    {
        _rows.Clear();
        _continuation = null;
        base.OnAccessDenied();
    }

    private static string ItemName(OwnedShareDto share) =>
        share.Name ?? (share.IsDamaged ? SharedItemText.UnreadableName : UnavailableItemText);

    private static string KindText(OwnedShareDto share) =>
        share.Kind == SharedItemKindDto.Folder ? SharedItemText.FolderKind : SecretKindText;

    private static string GrantedText(OwnedShareDto share) =>
        share.GrantedAt.UtcDateTime.ToString(GrantedFormat, CultureInfo.InvariantCulture);

    private string RevokeMessage => _revoking is null ? string.Empty : SharedItemStatusText.RevokeWarning(_revoking.RecipientDisplayName);

    private void AskRevoke(OwnedShareDto share) => _revoking = share;

    private void CancelRevoke() => _revoking = null;

    private Task ConfirmRevokeAsync()
    {
        var share = _revoking;
        if (share is null || _callerId is not { } owner)
        {
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => share.Kind == SharedItemKindDto.Folder
                ? Grants.RevokeFolderGrantAsync(owner, share.ItemId, share.RecipientId, token)
                : Grants.RevokeSecretGrantAsync(owner, share.ItemId, share.RecipientId, token),
            response => ApplyRevokeAsync(response, share),
            ShowRevokeFailed);
    }

    private async Task ApplyRevokeAsync(ApiResponse<bool> response, OwnedShareDto share)
    {
        _revoking = null;
        if (response.Status == ApiCallStatus.Success)
        {
            _rows.Remove(share);
            _status.Show(SharedItemStatusText.Revoked, AlertKind.Success);
            return;
        }

        if (response.Status == ApiCallStatus.NotFound)
        {
            _status.Show(GoneText, AlertKind.Warning);
            await ReloadAsync();
            return;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
    }

    private void ShowRevokeFailed()
    {
        _revoking = null;
        _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
    }

    private Task HandleLoadMore(MouseEventArgs args) =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => Grants.ListOwnedSharesAsync(PageSize, _continuation, token), AppendPage, LoadMoreFailedText);

    private void AppendPage(OwnedSharePageDto page)
    {
        _rows.AddRange(page.Items);
        _continuation = page.ContinuationToken;
    }
}
