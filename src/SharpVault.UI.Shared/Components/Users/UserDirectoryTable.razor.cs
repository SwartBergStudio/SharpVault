// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components.Web;
using SharpVault.Dtos.Users;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Users;

public sealed partial class UserDirectoryTable
{
    public const int PageSize = 50;
    public const int MaxRowsLoaded = 1000;
    public const string NeverText = "Never";
    public const string LoadMoreFailedText = "More users could not be loaded. Try again.";
    public const string LockedText = "User locked.";
    public const string UnlockedText = "User unlocked.";



    private readonly List<UserSummaryDto> _rows = [];
    private readonly StatusMessage _status = new();
    private string? _continuation;
    private Guid? _callerId;
    private bool _forbidden;
    private UserSummaryDto? _target;
    private UserSummaryDto? _countsUser;
    private bool _unlocking;

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

        var response = await UsersClient.ListUsersAsync(PageSize, null, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _rows.Clear();
            _rows.AddRange(response.Value.Users);
            _continuation = response.Value.ContinuationToken;
            LoadMoreFailed = false;
        }

        return response;
    }

    protected override bool IsEmpty(UserPageDto value) => !_forbidden && value.Users.Count == 0;

    protected override void OnAccessDenied()
    {
        _rows.Clear();
        _continuation = null;
        base.OnAccessDenied();
    }

    private Task HandleRefresh(MouseEventArgs args) => ReloadAsync();

    private Task HandleLoadMore(MouseEventArgs args) =>
        _continuation is null
            ? Task.CompletedTask
            : RunLoadMoreAsync(token => UsersClient.ListUsersAsync(PageSize, _continuation, token), AppendPage, LoadMoreFailedText);

    protected override bool HandleLoadMoreFailure(ApiCallStatus status) =>
        status == ApiCallStatus.ConfigurationMismatch && HandleFailureStatus(status);

    private void AppendPage(UserPageDto page)
    {
        _rows.AddRange(page.Users.Take(MaxRowsLoaded - _rows.Count));
        _continuation = page.ContinuationToken;
    }

    private void AskLock(UserSummaryDto user, bool unlock)
    {
        _target = user;
        _unlocking = unlock;
    }

    private void CancelLock() => _target = null;

    private void ShowCounts(UserSummaryDto user) => _countsUser = user;

    private void CloseCounts() => _countsUser = null;

    private string CountsTitle => $"Vault contents for {_countsUser?.DisplayName}";

    private Task ConfirmLockAsync(LockUserRequestDto request)
    {
        var target = _target;
        if (target is null)
        {
            return Task.CompletedTask;
        }

        var unlocking = _unlocking;
        return RunApiCommandAsync(
            token => unlocking
                ? UsersClient.UnlockAsync(target.UserId, request, token)
                : UsersClient.LockAsync(target.UserId, request, token),
            response => ApplyLockResponseAsync(response, unlocking),
            ShowLockFailed);
    }

    private Task ApplyLockResponseAsync(ApiResponse<UserSummaryDto> response, bool unlocking)
    {
        _target = null;
        if (response.Status == ApiCallStatus.Success && response.Value is { } updated)
        {
            ReplaceRow(updated);
            _status.Show(unlocking ? UnlockedText : LockedText, AlertKind.Success);
        }
        else
        {
            _status.Show(VaultStatusText.For(response), AlertKind.Danger);
        }

        return Task.CompletedTask;
    }

    private void ShowLockFailed()
    {
        _target = null;
        _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
    }

    private void ReplaceRow(UserSummaryDto updated)
    {
        var index = _rows.FindIndex(row => row.UserId == updated.UserId);
        if (index >= 0)
        {
            _rows[index] = updated;
        }
    }

    private static SecurityStatus StatusOf(UserSummaryDto user) =>
        user.Status == UserStatusDto.Active ? SecurityStatus.Active : SecurityStatus.Locked;

    private static string StatusLabel(UserSummaryDto user) =>
        user.Status == UserStatusDto.Active ? "Active" : "Locked";

    private static string LastSignIn(UserSummaryDto user) =>
        user.LastSignInAt <= DateTimeOffset.UnixEpoch
            ? NeverText
            : StampText.For(user.LastSignInAt);
}
