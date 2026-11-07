// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.OneTimeLinks;

namespace SharpVault.UI.Shared.Components.OneTimeLinks;

public sealed partial class OneTimeLinkEditWarning
{
    public const int MaxPagesRead = 5;
    public const string RevokedText = "The links were revoked.";
    public const string RevokeFailedText = "The links could not all be revoked. Try again.";

    private readonly StatusMessage _status = new();
    private bool _confirming;

    [Parameter, EditorRequired]
    public Guid SecretId { get; set; }

    public static string WarningText(int count) => count == 1
        ? "1 open one-time link holds a copy of this item. Changes you save here will not reach it."
        : $"{count} open one-time links hold a copy of this item. Changes you save here will not reach them.";

    protected override async Task<ApiResponse<OneTimeLinkPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var matches = new List<OneTimeLinkDto>();
        string? continuation = null;
        for (var page = 0; page < MaxPagesRead; page++)
        {
            var response = await Links.ListAsync(OneTimeLinkUiLimits.PageSize, continuation, cancellationToken);
            if (response.Status != ApiCallStatus.Success || response.Value is null)
            {
                return response;
            }

            matches.AddRange(response.Value.Items.Where(link => link.ItemId == SecretId));
            continuation = response.Value.ContinuationToken;
            if (continuation is null)
            {
                break;
            }
        }

        return ApiResponse<OneTimeLinkPageDto>.Success(new OneTimeLinkPageDto { Items = matches });
    }

    protected override bool IsEmpty(OneTimeLinkPageDto value) => value.Items.Count == 0;

    private void OpenRevoke() => _confirming = true;

    private void CloseRevoke() => _confirming = false;

    private Task ConfirmRevokeAsync() =>
        RunApiCommandAsync(RevokeAllAsync, ApplyRevokeAsync, ShowRevokeFailed);

    private async Task<ApiResponse<bool>> RevokeAllAsync(CancellationToken cancellationToken)
    {
        foreach (var link in Value?.Items ?? [])
        {
            var response = await Links.RevokeAsync(link.Id, cancellationToken);
            if (response.Status is not (ApiCallStatus.Success or ApiCallStatus.NotFound))
            {
                return response;
            }
        }

        return ApiResponse<bool>.Success(true);
    }

    private async Task ApplyRevokeAsync(ApiResponse<bool> response)
    {
        _confirming = false;
        if (response.Status == ApiCallStatus.Success)
        {
            _status.Show(RevokedText, AlertKind.Success);
        }
        else
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        }

        await ReloadAsync();
    }

    private void ShowRevokeFailed()
    {
        _confirming = false;
        _status.Show(RevokeFailedText, AlertKind.Danger);
    }
}
