// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Users;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed partial class TransferRequestForm
{
    public const int UserPageSize = 100;
    public const string SourceLockProblemText = "Lock the leaving user before you request this transfer.";
    public const string OpenTransferText = "This user already has an open transfer. Finish or abort it before you start another.";

    private static readonly IReadOnlyList<SvSelectOption> ScopeOptions =
    [
        new(TransferScopeKindDto.AllItems.ToString(), TransferStatusText.Scope(TransferScopeKindDto.AllItems)),
        new(TransferScopeKindDto.SelectedFolders.ToString(), TransferStatusText.Scope(TransferScopeKindDto.SelectedFolders)),
    ];

    private const string ReasonHelp = "Say why in at least 20 characters. The reason stays in the audit log and is never shown to the receiving user.";
    private const string PlaceholderValue = "";
    private const string PlaceholderText = "Choose a user";

    private readonly TransferFormInput _input = new();
    private readonly StatusMessage _status = new();
    private List<UserSummaryDto> _users = [];
    private Guid? _callerId;
    private TransferPreviewDto? _preview;
    private bool _forbidden;
    private bool _lockOpen;

    [Parameter]
    public Guid? SourceUserId { get; set; }

    [Inject]
    private NavigationManager Navigator { get; set; } = default!;

    private bool SourceNeedsLock => TransferFormInput.RequiresLockedSource(_input.Category) && _preview is { SourceStatus: UserStatusDto.Active };

    private string SourceValue => _input.SourceUserId?.ToString("D") ?? PlaceholderValue;

    private string TargetValue => _input.TargetUserId?.ToString("D") ?? PlaceholderValue;

    private IReadOnlyList<SvSelectOption> SourceOptions => UserOptions(_input.TargetUserId);

    private IReadOnlyList<SvSelectOption> TargetOptions => UserOptions(_input.SourceUserId);

    protected override async Task<ApiResponse<UserPageDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentUserAccessor.GetAsync(cancellationToken);
        if (current.Status != ApiCallStatus.Success || current.Value is null)
        {
            return new ApiResponse<UserPageDto>(current.Status, null);
        }

        _callerId = current.Value.UserId;
        _forbidden = !current.Value.IsAdmin;
        if (_forbidden)
        {
            return ApiResponse<UserPageDto>.Success(new UserPageDto());
        }

        var loaded = await LoadUsersAsync(cancellationToken);
        if (loaded.Status != ApiCallStatus.Success || loaded.Value is null)
        {
            return loaded;
        }

        _users = loaded.Value.Users.Where(user => user.UserId != _callerId).ToList();
        _input.SourceUserId = SourceUserId;
        if (SourceUserId is { } source)
        {
            ApplyPreview(await Transfers.PreviewAsync(source, cancellationToken));
        }

        return ApiResponse<UserPageDto>.Success(new UserPageDto { Users = _users });
    }

    protected override bool IsEmpty(UserPageDto value) => !_forbidden && value.Users.Count == 0;

    protected override void OnAccessDenied()
    {
        _users = [];
        base.OnAccessDenied();
    }

    private async Task<ApiResponse<UserPageDto>> LoadUsersAsync(CancellationToken cancellationToken)
    {
        var users = new List<UserSummaryDto>();
        string? continuation = null;
        do
        {
            var page = await UsersClient.ListUsersAsync(UserPageSize, continuation, cancellationToken);
            if (page.Status != ApiCallStatus.Success || page.Value is null)
            {
                return page;
            }

            users.AddRange(page.Value.Users);
            continuation = page.Value.ContinuationToken;
        }
        while (continuation is not null && users.Count < UserDirectoryTable.MaxRowsLoaded);

        return ApiResponse<UserPageDto>.Success(new UserPageDto { Users = users.Take(UserDirectoryTable.MaxRowsLoaded).ToList() });
    }

    private List<SvSelectOption> UserOptions(Guid? excluded) =>
    [
        new(PlaceholderValue, PlaceholderText),
        .. _users.Where(user => user.UserId != excluded).Select(user => new SvSelectOption(user.UserId.ToString("D"), UserLabel(user))),
    ];

    private static string UserLabel(UserSummaryDto user) =>
        user.Status == UserStatusDto.Active ? user.DisplayName : $"{user.DisplayName} (locked)";

    private Task SetSourceAsync(string value)
    {
        _input.SourceUserId = ParseUser(value);
        _input.FolderIds.Clear();
        _preview = null;
        return _input.SourceUserId is { } source
            ? RunApiCommandAsync(token => Transfers.PreviewAsync(source, token), ApplyPreviewAsync, ShowPreviewFailed)
            : InvokeStateChangedAsync();
    }

    private Task ApplyPreviewAsync(ApiResponse<TransferPreviewDto> response)
    {
        ApplyPreview(response);
        return Task.CompletedTask;
    }

    private void ApplyPreview(ApiResponse<TransferPreviewDto> response)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is { } preview)
        {
            _preview = preview;
            _input.Scope = TransferFormInput.DefaultScopeFor(_input.Category);
            if (preview.HasOpenTransfer)
            {
                _status.Show(OpenTransferText, AlertKind.Warning);
            }
            else
            {
                _status.Clear();
            }

            return;
        }

        _preview = null;
        if (!TryHandleSessionStatus(response.Status))
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        }
    }

    private void ShowPreviewFailed()
    {
        _preview = null;
        _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
    }

    private void SetTarget(string value) => _input.TargetUserId = ParseUser(value);

    private void SetCategory(string value)
    {
        if (!ReasonCategoryLabels.TryParse(value, out var category))
        {
            return;
        }

        _input.Category = category;
        _input.Scope = TransferFormInput.DefaultScopeFor(category);
        _input.FolderIds.Clear();
    }

    private void SetScope(string value)
    {
        if (!Enum.TryParse(value, out TransferScopeKindDto scope) || !Enum.IsDefined(scope))
        {
            return;
        }

        _input.Scope = scope;
        if (scope == TransferScopeKindDto.AllItems)
        {
            _input.FolderIds.Clear();
        }
    }

    private void SetReason(string value) => _input.Reason = value;

    private void ToggleFolder(Guid folderId)
    {
        if (!_input.FolderIds.Remove(folderId))
        {
            _input.FolderIds.Add(folderId);
        }
    }

    private void OpenLockSource(MouseEventArgs args) => _lockOpen = true;

    private void CancelLock() => _lockOpen = false;

    private Task ConfirmLockAsync(LockUserRequestDto request)
    {
        var source = _input.SourceUserId;
        if (source is null)
        {
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => UsersClient.LockAsync(source.Value, request, token),
            response => ApplyLockAsync(response, source.Value),
            ShowLockFailed);
    }

    private async Task ApplyLockAsync(ApiResponse<UserSummaryDto> response, Guid source)
    {
        _lockOpen = false;
        if (response.Status != ApiCallStatus.Success || response.Value is null)
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
            return;
        }

        ReplaceUser(response.Value);
        _status.Show(UserDirectoryTable.LockedText, AlertKind.Success);
        ApplyPreview(await Transfers.PreviewAsync(source, Cts.Token));
    }

    private void ShowLockFailed()
    {
        _lockOpen = false;
        _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
    }

    private void ReplaceUser(UserSummaryDto updated)
    {
        var index = _users.FindIndex(user => user.UserId == updated.UserId);
        if (index >= 0)
        {
            _users[index] = updated;
        }
    }

    private Task HandleSubmitAsync(MouseEventArgs args)
    {
        var problem = _input.FirstProblem() ?? (SourceNeedsLock ? SourceLockProblemText : null);
        if (problem is not null)
        {
            _status.Show(problem, AlertKind.Danger);
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            token => Transfers.CreateAsync(_input.ToRequest(), token),
            ApplySubmitAsync,
            () => _status.Show(VaultStatusText.FailedText, AlertKind.Danger));
    }

    private Task ApplySubmitAsync(ApiResponse<TransferDto> response)
    {
        if (response.Status == ApiCallStatus.Success && response.Value is { } created)
        {
            Navigator.NavigateTo(TransferRoutes.Transfer(created.Id));
            return Task.CompletedTask;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        return Task.CompletedTask;
    }

    private static Guid? ParseUser(string value) => Guid.TryParse(value, out var id) ? id : null;
}
