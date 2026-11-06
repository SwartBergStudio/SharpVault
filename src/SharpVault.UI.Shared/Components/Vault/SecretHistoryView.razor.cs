// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UI.Shared.Components.Vault;

public sealed partial class SecretHistoryView
{
    [Inject]
    private IVaultSecretsApiClient Secrets { get; set; } = default!;

    [Inject]
    private ISecretHistoryApiClient History { get; set; } = default!;

    [Inject]
    private VaultUiSettings Settings { get; set; } = default!;

    public const string RestoredText = "Version restored.";
    public const string UnreadableSecretText = "an unreadable secret";

    private SecretDto? _secret;
    private SecretVersionDto? _restoreTarget;
    private readonly StatusMessage _status = new();
    private bool _conflict;

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    private string HeadingText => _secret is null
        ? "History"
        : $"History of {(string.IsNullOrWhiteSpace(_secret.Name) ? UnreadableSecretText : _secret.Name)}";

    protected override async Task<ApiResponse<SecretVersionListDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var secret = await Secrets.GetSecretAsync(SecretId, cancellationToken);
        if (secret.Status != ApiCallStatus.Success || secret.Value is null)
        {
            return new ApiResponse<SecretVersionListDto>(secret.Status, null);
        }

        var versions = await History.ListVersionsAsync(SecretId, cancellationToken);
        if (versions.Status == ApiCallStatus.Success)
        {
            _secret = secret.Value;
        }

        return versions;
    }

    protected override bool IsEmpty(SecretVersionListDto value) => value.Items.Count == 0;

    private static string ReplacedText(SecretVersionDto version) =>
        StampText.For(version.ReplacedAt);

    private Func<RevealIntentDto, CancellationToken, Task<ApiResponse<RevealedValueDto>>> RevealerFor(SecretVersionDto version) =>
        (intent, token) => History.RevealVersionAsync(SecretId, version.VersionId, new RevealVersionRequestDto { Intent = intent }, token);

    private async Task HandleRevealFailureAsync(ApiCallStatus status)
    {
        if (!TryHandleSessionStatus(status))
        {
            _status.Show(VaultStatusText.For(new ApiResponse<bool>(status, false)), AlertKind.Danger);
        }

        await InvokeStateChangedAsync();
    }

    private void AskRestore(SecretVersionDto version) => _restoreTarget = version;

    private void CancelRestore() => _restoreTarget = null;

    private async Task HandleConflictReload(Microsoft.AspNetCore.Components.Web.MouseEventArgs args)
    {
        _conflict = false;
        await ReloadAsync();
    }

    private Task ConfirmRestoreAsync()
    {
        var target = _restoreTarget;
        if (target is null || _secret is null)
        {
            return Task.CompletedTask;
        }

        var token = _secret.ConcurrencyToken;
        return RunApiCommandAsync(
            cancellationToken => History.RestoreVersionAsync(SecretId, target.VersionId, token, cancellationToken),
            ApplyRestoreAsync,
            () =>
            {
                _restoreTarget = null;
                _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
            });
    }

    private async Task ApplyRestoreAsync(ApiResponse<SecretDto> response)
    {
        _restoreTarget = null;
        switch (response.Status)
        {
            case ApiCallStatus.Success:
                _conflict = false;
                _status.Show(RestoredText, AlertKind.Success);
                await ReloadAsync();
                break;
            case ApiCallStatus.Conflict:
                _conflict = true;
                _status.Clear();
                break;
            default:
                _status.Show(VaultStatusText.For(response), AlertKind.Danger);
                break;
        }
    }

    private void ClearFailure() => _status.ClearFailure();

    private void ReportCopied() => _status.Show(SecretStatusText.Copied(Settings), AlertKind.Info, expires: true);
}
