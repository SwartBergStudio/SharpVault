// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class SharedSecretDetailView
{
    private readonly StatusMessage _status = new();

    [Parameter, EditorRequired]
    public Guid OwnerId { get; set; }

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    [Inject]
    private ISharedItemsApiClient SharedItems { get; set; } = default!;

    [Inject]
    private VaultUiSettings Settings { get; set; } = default!;

    protected override Task<ApiResponse<SharedSecretDto>> LoadAsync(CancellationToken cancellationToken) =>
        SharedItems.GetSecretAsync(OwnerId, SecretId, cancellationToken);

    private static bool CanEdit(SharedSecretDto shared) =>
        !shared.Item.IsDamaged && shared.Permission is SharePermissionDto.Edit or SharePermissionDto.Manage;

    private Task<ApiResponse<RevealedValueDto>> RevealFieldAsync(RevealFieldDto field, RevealIntentDto intent, CancellationToken cancellationToken) =>
        SharedItems.RevealSecretAsync(OwnerId, SecretId, new RevealSecretRequestDto { Field = field, Intent = intent }, cancellationToken);

    private void ReportFailure(ApiCallStatus status)
    {
        if (TryHandleSessionStatus(status))
        {
            return;
        }

        _status.Show(SharedLoadErrors.ForCommand(new ApiResponse<bool>(status, false)), AlertKind.Danger);
    }

    private void ClearFailure() => _status.ClearFailure();

    private void ReportInfo(string text) => _status.Show(text, AlertKind.Info, expires: true);
}
