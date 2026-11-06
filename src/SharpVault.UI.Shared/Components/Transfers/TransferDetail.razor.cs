// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Base;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared.Components.Transfers;

public sealed partial class TransferDetail
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    public const string ChangedText = "This transfer changed. It has been reloaded.";
    public const string ApprovedText = "Approved. The items start moving shortly.";
    public const string RejectedText = "The transfer was rejected.";
    public const string WithdrawnText = "The transfer was withdrawn.";
    public const string AbortedText = "The transfer was aborted. Items that already moved stay where they are.";

    private TransferDto? _transfer;
    private Guid? _callerId;
    private readonly StatusMessage _status = new();
    private PeriodicTimer? _poller;
    private bool _forbidden;
    private bool _approveOpen;
    private bool _rejectOpen;
    private bool _abortOpen;

    [Parameter, EditorRequired]
    public Guid TransferId { get; set; }

    private string RejectLabelText => _transfer is not null && _transfer.RequestedBy == _callerId ? "Withdraw" : "Reject";

    private string RejectTitle => RejectLabelText == "Withdraw" ? "Withdraw this transfer?" : "Reject this transfer?";

    private string ApproveSummary => _transfer is null
        ? string.Empty
        : $"Approving starts moving {_transfer.ItemCount} items and {_transfer.FolderCount} folders from {_transfer.SourceDisplayName} to {_transfer.TargetDisplayName}. Once the items start moving, the transfer runs to the end.";

    protected override async Task<ApiResponse<TransferDto>> LoadAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentUserAccessor.GetAsync(cancellationToken);
        if (current.Status != ApiCallStatus.Success || current.Value is null)
        {
            return new ApiResponse<TransferDto>(current.Status, null);
        }

        _callerId = current.Value.UserId;
        _forbidden = !current.Value.IsAdmin;
        if (_forbidden)
        {
            return ApiResponse<TransferDto>.Success(new TransferDto());
        }

        var response = await Transfers.GetTransferAsync(TransferId, cancellationToken);
        if (response.Status == ApiCallStatus.Success && response.Value is not null)
        {
            _transfer = response.Value;
            EnsurePolling();
        }

        return response;
    }

    protected override void OnAccessDenied()
    {
        _transfer = null;
        base.OnAccessDenied();
    }

    private static bool ShowProgress(TransferDto transfer) =>
        TransferStatusText.IsInProgress(transfer.State)
        || transfer.State is TransferStateDto.Completed or TransferStateDto.Blocked;

    private Task HandleRefresh(MouseEventArgs args) => ReloadAsync();

    private void OpenApprove(MouseEventArgs args) => _approveOpen = true;

    private void OpenReject(MouseEventArgs args) => _rejectOpen = true;

    private void OpenAbort(MouseEventArgs args) => _abortOpen = true;

    private void CloseDialogs()
    {
        _approveOpen = false;
        _rejectOpen = false;
        _abortOpen = false;
    }

    private Task ConfirmApproveAsync() => ExecuteAsync(
        token => Transfers.ApproveAsync(TransferId, CurrentToken(), token),
        ApprovedText);

    private Task ConfirmRejectAsync(string reason) => ExecuteAsync(
        token => Transfers.RejectAsync(TransferId, CurrentToken(), new TransferDecisionRequestDto { Reason = reason }, token),
        RejectLabelText == "Withdraw" ? WithdrawnText : RejectedText);

    private Task ConfirmAbortAsync(string reason) => ExecuteAsync(
        token => Transfers.AbortAsync(TransferId, CurrentToken(), new TransferDecisionRequestDto { Reason = reason }, token),
        AbortedText);

    private string CurrentToken() => _transfer?.ConcurrencyToken ?? string.Empty;

    private Task ExecuteAsync(Func<CancellationToken, Task<ApiResponse<TransferDto>>> call, string successText)
    {
        if (_transfer is null)
        {
            return Task.CompletedTask;
        }

        return RunApiCommandAsync(
            call,
            response => ApplyActionAsync(response, successText),
            () =>
            {
                CloseDialogs();
                _status.Show(VaultStatusText.FailedText, AlertKind.Danger);
            });
    }

    private async Task ApplyActionAsync(ApiResponse<TransferDto> response, string successText)
    {
        CloseDialogs();
        if (response.Status == ApiCallStatus.Success && response.Value is { } updated)
        {
            _transfer = updated;
            _status.Show(successText, AlertKind.Success);
            EnsurePolling();
            return;
        }

        if (response.Status == ApiCallStatus.Conflict)
        {
            _status.Show(ChangedText, AlertKind.Warning);
            await ReloadAsync();
            return;
        }

        _status.Show(VaultStatusText.For(response), AlertKind.Danger);
    }

    private void EnsurePolling()
    {
        if (_poller is not null || _transfer is null || !TransferStatusText.IsInProgress(_transfer.State))
        {
            return;
        }

        var timer = Track(new PeriodicTimer(PollInterval, Time));
        _poller = timer;
        _ = PollAsync(timer);
    }

    private async Task PollAsync(PeriodicTimer timer)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(Cts.Token))
            {
                await RefreshQuietlyAsync(Cts.Token);
                if (_transfer is null || !TransferStatusText.IsInProgress(_transfer.State))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
        }
    }

    private async Task RefreshQuietlyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await Transfers.GetTransferAsync(TransferId, cancellationToken);
            if (response.Status == ApiCallStatus.Success && response.Value is { } latest)
            {
                _transfer = latest;
                await InvokeStateChangedAsync();
                return;
            }

            TryHandleSessionStatus(response.Status);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogPollFailed(Logger, exception.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Polling a transfer failed with {ExceptionType}")]
    private static partial void LogPollFailed(ILogger logger, string exceptionType);
}
