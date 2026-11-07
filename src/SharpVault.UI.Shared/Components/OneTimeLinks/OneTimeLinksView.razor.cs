// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.OneTimeLinks;

namespace SharpVault.UI.Shared.Components.OneTimeLinks;

public sealed partial class OneTimeLinksView
{
    public const string LoadMoreFailedText = "More links could not be loaded. Try again.";
    public const string RevokedText = "The link was revoked.";
    public const string RevokeFailedText = "The link could not be revoked. Try again.";

    private readonly List<OneTimeLinkDto> _rows = [];
    private readonly StatusMessage _status = new();
    private string? _continuation;
    private Guid? _pendingRevoke;

    protected override async Task<ApiResponse<OneTimeLinkPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var response = await Links.ListAsync(OneTimeLinkUiLimits.PageSize, null, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Items);
            _continuation = response.Value.ContinuationToken;
            LoadMoreFailed = false;
        }

        return response;
    }

    protected override bool IsEmpty(OneTimeLinkPageDto value) =>
        value.Items.Count == 0 && value.ContinuationToken is null;

    public static string NameOf(OneTimeLinkDto link) => OneTimeLinkText.NameOf(link.ItemName);

    public static string CreatedText(OneTimeLinkDto link) => StampText.For(link.CreatedAt);

    public static string ExpiresText(OneTimeLinkDto link) => StampText.For(link.ExpiresAt);

    private Task HandleLoadMoreAsync() =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => Links.ListAsync(OneTimeLinkUiLimits.PageSize, _continuation, token), AppendPage, LoadMoreFailedText);

    private void AppendPage(OneTimeLinkPageDto page)
    {
        _rows.AddRange(page.Items);
        _continuation = page.ContinuationToken;
    }

    private void OpenRevoke(Guid linkId) => _pendingRevoke = linkId;

    private void CloseRevoke() => _pendingRevoke = null;

    private Task ConfirmRevokeAsync()
    {
        if (_pendingRevoke is not { } linkId)
        {
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => Links.RevokeAsync(linkId, token),
            response => ApplyRevokeAsync(response, linkId),
            ShowRevokeFailed);
    }

    private Task ApplyRevokeAsync(ApiResponse<bool> response, Guid linkId)
    {
        _pendingRevoke = null;
        if (response.Status is ApiCallStatus.Success or ApiCallStatus.NotFound)
        {
            _rows.RemoveAll(link => link.Id == linkId);
            _status.Show(RevokedText, AlertKind.Success);
            return Task.CompletedTask;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        return Task.CompletedTask;
    }

    private void ShowRevokeFailed()
    {
        _pendingRevoke = null;
        _status.Show(RevokeFailedText, AlertKind.Danger);
    }
}
