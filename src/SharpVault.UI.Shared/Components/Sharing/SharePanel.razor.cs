// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed partial class SharePanel
{
    public const string DefaultIdPrefix = "share";

    private readonly StatusMessage _status = new();
    private bool _addOpen;
    private string? _addError;
    private ShareGrantDto? _revoking;

    [Parameter, EditorRequired]
    public Guid OwnerId { get; set; }

    [Parameter, EditorRequired]
    public SharedItemKindDto Kind { get; set; }

    [Parameter, EditorRequired]
    public Guid ItemId { get; set; }

    [Parameter]
    public string IdPrefix { get; set; } = DefaultIdPrefix;

    [Inject]
    private IShareGrantsApiClient Grants { get; set; } = default!;

    [Inject]
    private IDirectoryApiClient DirectoryClient { get; set; } = default!;

    private string RevokeText => _revoking is null ? string.Empty : SharedItemStatusText.RevokeWarning(_revoking.RecipientDisplayName);

    protected override void OnParametersSet() => ComponentIds.EnsureValid(IdPrefix, nameof(IdPrefix));

    protected override Task<ApiResponse<ShareGrantListDto>> LoadAsync(CancellationToken cancellationToken) =>
        Kind == SharedItemKindDto.Folder
            ? Grants.ListFolderGrantsAsync(OwnerId, ItemId, cancellationToken)
            : Grants.ListSecretGrantsAsync(OwnerId, ItemId, cancellationToken);

    protected override bool IsEmpty(ShareGrantListDto value) => value.Items.Count == 0;

    private string IdFor(string suffix) => $"{IdPrefix}-{suffix}";

    private string GrantId(string part, ShareGrantDto grant) => $"{IdPrefix}-grant-{part}-{grant.RecipientId:D}";

    private void OpenAdd()
    {
        _addError = null;
        _addOpen = true;
    }

    private void CloseAdd() => _addOpen = false;

    private void OpenRevoke(ShareGrantDto grant) => _revoking = grant;

    private void CloseRevoke() => _revoking = null;

    private async Task<ApiResponse<DirectoryUserListDto>> SearchAsync(string term, CancellationToken cancellationToken)
    {
        var response = await DirectoryClient.SearchUsersAsync(term, cancellationToken);
        TryHandleSessionStatus(response.Status);
        return response;
    }

    private Task AddAsync(ShareDialogInput input)
    {
        _addError = null;
        var request = new CreateShareGrantRequestDto { RecipientId = input.Recipient.Id, Permission = input.Permission };
        return RunApiCommandAsync(
            token => Kind == SharedItemKindDto.Folder
                ? Grants.CreateFolderGrantAsync(OwnerId, ItemId, request, token)
                : Grants.CreateSecretGrantAsync(OwnerId, ItemId, request, token),
            ApplyAddAsync,
            () => _addError = VaultStatusText.FailedText);
    }

    private async Task ApplyAddAsync(ApiResponse<ShareGrantDto> response)
    {
        switch (response.Status)
        {
            case ApiCallStatus.Success:
                _addOpen = false;
                _status.Show(SharedItemStatusText.Shared, AlertKind.Success);
                await ReloadAsync();
                break;
            case ApiCallStatus.Conflict:
                _addOpen = false;
                _status.Show(VaultStatusText.ConflictText, AlertKind.Danger);
                await ReloadAsync();
                break;
            default:
                _addError = VaultStatusText.For(response);
                break;
        }
    }

    private Task ChangeAsync(ShareGrantDto grant, string? value)
    {
        var permission = PermissionLabels.Parse(value, grant.Permission);
        if (permission == grant.Permission || IsCommandRunning)
        {
            return Task.CompletedTask;
        }

        var request = new ChangeShareGrantRequestDto { Permission = permission };
        return RunApiCommandAsync(
            token => Kind == SharedItemKindDto.Folder
                ? Grants.ChangeFolderGrantAsync(OwnerId, ItemId, grant.RecipientId, grant.ConcurrencyToken, request, token)
                : Grants.ChangeSecretGrantAsync(OwnerId, ItemId, grant.RecipientId, grant.ConcurrencyToken, request, token),
            ApplyChangeAsync,
            ShowFailed);
    }

    private async Task ApplyChangeAsync(ApiResponse<ShareGrantDto> response)
    {
        if (response.Status == ApiCallStatus.Success)
        {
            _status.Show(SharedItemStatusText.PermissionChanged, AlertKind.Success);
        }
        else
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        }

        await ReloadAsync();
    }

    private Task RevokeAsync()
    {
        if (_revoking is not { } grant)
        {
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => Kind == SharedItemKindDto.Folder
                ? Grants.RevokeFolderGrantAsync(OwnerId, ItemId, grant.RecipientId, token)
                : Grants.RevokeSecretGrantAsync(OwnerId, ItemId, grant.RecipientId, token),
            ApplyRevokeAsync,
            () =>
            {
                _revoking = null;
                ShowFailed();
            });
    }

    private async Task ApplyRevokeAsync(ApiResponse<bool> response)
    {
        _revoking = null;
        if (response.Status == ApiCallStatus.Success)
        {
            _status.Show(SharedItemStatusText.Revoked, AlertKind.Success);
        }
        else
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        }

        await ReloadAsync();
    }

    private void ShowFailed() => _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
}
