// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UI.Shared.Components.Vault;

public sealed partial class SecretDetailView
{
    private IReadOnlyList<FolderDto> _folders = [];
    private readonly StatusMessage _status = new();
    private string? _moveError;
    private bool _deleteOpen;
    private bool _moveOpen;
    private bool _foldersUnavailable;

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    [CascadingParameter(Name = LayoutCascades.CurrentUser)]
    private CurrentUserDto? CurrentUser { get; set; }

    [Inject]
    private IVaultSecretsApiClient Secrets { get; set; } = default!;

    [Inject]
    private IVaultFoldersApiClient FoldersClient { get; set; } = default!;

    [Inject]
    private VaultUiSettings Settings { get; set; } = default!;

    [Inject]
    private TimeProvider Time { get; set; } = default!;

    [Inject]
    private NavigationManager Navigator { get; set; } = default!;

    protected override async Task<ApiResponse<SecretDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var secret = await Secrets.GetSecretAsync(SecretId, cancellationToken);
        if (secret.Status != ApiCallStatus.Success || secret.Value is null)
        {
            return secret;
        }

        var folders = await FoldersClient.ListFoldersAsync(cancellationToken);
        if (folders.Status.FailsTheWholeView())
        {
            return new ApiResponse<SecretDto>(folders.Status, null);
        }

        _foldersUnavailable = folders.Status != ApiCallStatus.Success || folders.Value is null;
        _folders = _foldersUnavailable ? [] : folders.Value!.Items;
        return secret;
    }

    private string? FolderNameOf(SecretDto secret)
    {
        if (secret.FolderId is not { } folderId)
        {
            return null;
        }

        var folder = _folders.FirstOrDefault(candidate => candidate.Id == folderId);
        return _foldersUnavailable || folder is null ? FolderNodes.DamagedName : FolderNodes.NameOf(folder);
    }

    private Task<ApiResponse<RevealedValueDto>> RevealFieldAsync(RevealFieldDto field, RevealIntentDto intent, CancellationToken cancellationToken) =>
        Secrets.RevealSecretAsync(SecretId, new RevealSecretRequestDto { Field = field, Intent = intent }, cancellationToken);

    private void ReportFailure(ApiCallStatus status)
    {
        if (TryHandleSessionStatus(status))
        {
            return;
        }

        _status.Show(VaultStatusText.For(new ApiResponse<bool>(status, false)), AlertKind.Danger);
    }

    private void ClearFailure() => _status.ClearFailure();

    private void ReportInfo(string text) => _status.Show(text, AlertKind.Info, expires: true);

    private void OpenDelete() => _deleteOpen = true;

    private void CloseDelete() => _deleteOpen = false;

    private void OpenMove()
    {
        _moveError = null;
        _moveOpen = true;
    }

    private void CloseMove() => _moveOpen = false;

    private Task DeleteAsync()
    {
        if (Value is null)
        {
            return Task.CompletedTask;
        }

        var token = Value.ConcurrencyToken;
        return RunApiCommandAsync(
            cancellationToken => Secrets.DeleteSecretAsync(SecretId, token, cancellationToken),
            ApplyDelete,
            ShowFailed);
    }

    private Task ApplyDelete(ApiResponse<bool> response)
    {
        if (response.Status == ApiCallStatus.Success)
        {
            Navigator.NavigateTo(VaultRoutes.Vault);
            return Task.CompletedTask;
        }

        _deleteOpen = false;
        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        return Task.CompletedTask;
    }

    private Task MoveAsync(Guid? folderId)
    {
        if (Value is null || IsCommandRunning)
        {
            return Task.CompletedTask;
        }

        _moveError = null;
        var token = Value.ConcurrencyToken;
        return RunApiCommandAsync(
            cancellationToken => Secrets.MoveSecretAsync(SecretId, token, new MoveSecretRequestDto { FolderId = folderId }, cancellationToken),
            ApplyMoveAsync,
            ShowFailed);
    }

    private async Task ApplyMoveAsync(ApiResponse<SecretDto> response)
    {
        switch (response.Status)
        {
            case ApiCallStatus.Success:
                _moveOpen = false;
                _status.Show(SecretStatusText.MovedText, AlertKind.Success);
                await ReloadAsync();
                break;
            case ApiCallStatus.Conflict:
                _moveOpen = false;
                _status.Show(VaultStatusText.For(response), AlertKind.Danger);
                break;
            default:
                _moveError = VaultStatusText.For(response);
                break;
        }
    }

    private void ShowFailed() => _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
}
