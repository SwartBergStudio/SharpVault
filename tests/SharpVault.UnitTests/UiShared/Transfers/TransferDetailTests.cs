// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Pages.Transfers;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferDetailTests : TransferUiTestHost
{
    private const string RejectReason = "Wrong target, please withdraw";

    private readonly Guid _transferId = Guid.NewGuid();

    private IRenderedComponent<AdminTransferDetailPage> RenderDetail(TransferDto transfer)
    {
        Transfers.GetTransferAsync(_transferId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferDto>.Success(transfer)));
        return Render<AdminTransferDetailPage>(parameters => parameters.Add(page => page.TransferId, _transferId));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Requester_SeesTheSelfNoteAndWithdrawButNoApprove()
    {
        var cut = RenderDetail(Transfer(_transferId, CallerId, canApprove: false, canReject: true));

        Assert.NotNull(cut.Find("#transfer-self-note"));
        Assert.Equal("Withdraw", cut.Find("#transfer-reject").TextContent.Trim());
        Assert.Empty(cut.FindAll("#transfer-approve"));
    }

    [Fact]
    public void SecondAdmin_SeesApproveAndReject()
    {
        var otherAdmin = Guid.NewGuid();
        var cut = RenderDetail(Transfer(_transferId, otherAdmin, canApprove: true, canReject: true));

        Assert.Empty(cut.FindAll("#transfer-self-note"));
        Assert.NotNull(cut.Find("#transfer-approve"));
        Assert.Equal("Reject", cut.Find("#transfer-reject").TextContent.Trim());
    }

    [Fact]
    public void AbortIsHidden_WhenCanAbortIsFalse()
    {
        var cut = RenderDetail(Transfer(_transferId, Guid.NewGuid(), canAbort: false));

        Assert.Empty(cut.FindAll("#transfer-abort"));
    }

    [Fact]
    public void Blocked_ShowsTheBlockReasonAndAbort()
    {
        var blocked = Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Blocked, canAbort: true);
        blocked.BlockReason = TransferBlockReasonDto.TargetLocked;

        var cut = RenderDetail(blocked);

        Assert.NotNull(cut.Find("#transfer-block-reason"));
        Assert.NotNull(cut.Find("#transfer-abort"));
    }

    [Fact]
    public void Retrying_ShowsTheFailureKindAndWhenItStartedFailing()
    {
        var retrying = Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Publishing, canAbort: true);
        retrying.IsRetrying = true;
        retrying.LastFailureKind = "TransferStepPendingException";
        retrying.FailingSince = Time.GetUtcNow();

        var cut = RenderDetail(retrying);

        Assert.Contains("TransferStepPendingException", cut.Find("#transfer-failure-kind").TextContent, StringComparison.Ordinal);
        Assert.NotNull(cut.Find("#transfer-failing-since"));
    }

    [Fact]
    public void RenamedSecrets_ShowTheCountAsASentence()
    {
        var completed = Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Completed);
        completed.RenamedItemCount = 2;

        var cut = RenderDetail(completed);

        Assert.Contains("2 secret(s) were already at the receiving user", cut.Find("#transfer-progress-renamed").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_ConfirmThenCallsWithTheConcurrencyToken()
    {
        var provider = RenderDialogProviders();
        var otherAdmin = Guid.NewGuid();
        var transfer = Transfer(_transferId, otherAdmin, canApprove: true, concurrencyToken: "etag-approve");
        Transfers.ApproveAsync(_transferId, "etag-approve", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferDto>.Success(transfer)));
        var cut = RenderDetail(transfer);

        await cut.Find("#transfer-approve").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-approve-dialog-confirm").ClickAsync(new MouseEventArgs());

        _ = Transfers.Received(1).ApproveAsync(_transferId, "etag-approve", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approve_Cancel_CallsNothing()
    {
        var provider = RenderDialogProviders();
        var cut = RenderDetail(Transfer(_transferId, Guid.NewGuid(), canApprove: true));

        await cut.Find("#transfer-approve").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-approve-dialog-cancel").ClickAsync(new MouseEventArgs());

        _ = Transfers.DidNotReceiveWithAnyArgs().ApproveAsync(default, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Approve_Conflict_ReloadsTheTransferAndSaysItChanged()
    {
        var provider = RenderDialogProviders();
        var transfer = Transfer(_transferId, Guid.NewGuid(), canApprove: true);
        var cut = RenderDetail(transfer);
        Transfers.ApproveAsync(_transferId, transfer.ConcurrencyToken, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferDto>.Conflict()));

        await cut.Find("#transfer-approve").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-approve-dialog-confirm").ClickAsync(new MouseEventArgs());

        Assert.Contains("This transfer changed. It has been reloaded.", cut.Find("#transfer-status").TextContent, StringComparison.Ordinal);
        _ = Transfers.Received(2).GetTransferAsync(_transferId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reject_WithoutAReason_CallsNothing()
    {
        var provider = RenderDialogProviders();
        var cut = RenderDetail(Transfer(_transferId, Guid.NewGuid(), canReject: true));

        await cut.Find("#transfer-reject").ClickAsync(new MouseEventArgs());
        await provider.Find("#transfer-reject-dialog-confirm").ClickAsync(new MouseEventArgs());

        _ = Transfers.DidNotReceiveWithAnyArgs().RejectAsync(default, default!, default!, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Reject_WithAReason_CallsRejectWithTheConcurrencyToken()
    {
        var provider = RenderDialogProviders();
        var transfer = Transfer(_transferId, Guid.NewGuid(), canReject: true, concurrencyToken: "etag-reject");
        var cut = RenderDetail(transfer);
        Transfers.RejectAsync(_transferId, "etag-reject", Arg.Any<TransferDecisionRequestDto>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferDto>.Success(transfer)));

        await cut.Find("#transfer-reject").ClickAsync(new MouseEventArgs());
        provider.Find("#transfer-reject-dialog-reason").Input(RejectReason);
        await provider.Find("#transfer-reject-dialog-confirm").ClickAsync(new MouseEventArgs());

        _ = Transfers.Received(1).RejectAsync(
            _transferId,
            "etag-reject",
            Arg.Is<TransferDecisionRequestDto>(request => request.Reason == RejectReason),
            Arg.Any<CancellationToken>());
    }

    private FakeTimeProvider Time => Services.GetRequiredService<FakeTimeProvider>();

    private static Task<ApiResponse<TransferDto>> Respond(TransferDto transfer) =>
        Task.FromResult(ApiResponse<TransferDto>.Success(transfer));

    private static Task<ApiResponse<TransferDto>> Failure() => Task.FromResult(ApiResponse<TransferDto>.Failed());

    private IRenderedComponent<AdminTransferDetailPage> RenderPolling(
        Task<ApiResponse<TransferDto>> first,
        params Task<ApiResponse<TransferDto>>[] later)
    {
        Transfers.GetTransferAsync(_transferId, Arg.Any<CancellationToken>()).Returns(first, later);
        return Render<AdminTransferDetailPage>(parameters => parameters.Add(page => page.TransferId, _transferId));
    }

    private int PollCount() =>
        Transfers.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(IAdminTransfersApiClient.GetTransferAsync));

    private static string StateText(IRenderedComponent<AdminTransferDetailPage> cut) => cut.Find("#transfer-state").TextContent.Trim();

    private async Task SettleAsync()
    {
        for (var attempt = 0; attempt < 100 && ThreadPool.PendingWorkItemCount > 0; attempt++)
        {
            await Task.Yield();
        }

        await Renderer.Dispatcher.InvokeAsync(static () => { });
    }

    [Theory]
    [InlineData(TransferStateDto.PendingApproval)]
    [InlineData(TransferStateDto.Completed)]
    [InlineData(TransferStateDto.Aborted)]
    public async Task Polling_NotInProgressAtLoad_DoesNotStartPolling(TransferStateDto state)
    {
        RenderPolling(Respond(Transfer(_transferId, Guid.NewGuid(), state)));

        Time.Advance(TransferDetail.PollInterval * 3);
        await SettleAsync();

        Assert.Equal(1, PollCount());
    }

    [Fact]
    public void Polling_InProgressTransfer_ShowsTheStateFromTheNextPoll()
    {
        var cut = RenderPolling(
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)),
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Releasing)));
        Assert.Equal("Approved", StateText(cut));

        Time.Advance(TransferDetail.PollInterval);

        cut.WaitForAssertion(() => Assert.Equal("Moving items", StateText(cut)));
    }

    [Fact]
    public void Polling_InProgressTransfer_RequestsTheTransferOncePerInterval()
    {
        var cut = RenderPolling(
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)),
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Releasing)));

        Time.Advance(TransferDetail.PollInterval);
        cut.WaitForAssertion(() => Assert.Equal(2, PollCount()));

        Time.Advance(TransferDetail.PollInterval);
        cut.WaitForAssertion(() => Assert.Equal(3, PollCount()));
    }

    [Fact]
    public async Task Polling_ReachesATerminalState_StopsRequestingTheTransfer()
    {
        var cut = RenderPolling(
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)),
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Completed)));

        Time.Advance(TransferDetail.PollInterval);
        cut.WaitForAssertion(() => Assert.Equal("Completed", StateText(cut)));
        await SettleAsync();
        var pollsAtTerminal = PollCount();

        Time.Advance(TransferDetail.PollInterval * 3);
        await SettleAsync();

        Assert.Equal(pollsAtTerminal, PollCount());
    }

    [Fact]
    public async Task Polling_ComponentDisposed_StopsRequestingTheTransfer()
    {
        Transfers.GetTransferAsync(_transferId, Arg.Any<CancellationToken>())
            .Returns(Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)));
        var host = Render<TransferDetailHost>(parameters => parameters.Add(component => component.TransferId, _transferId));

        Time.Advance(TransferDetail.PollInterval);
        host.WaitForAssertion(() => Assert.Equal(2, PollCount()));

        host.Render(parameters => parameters.Add(component => component.Show, false));
        await SettleAsync();
        var pollsAtDispose = PollCount();

        Time.Advance(TransferDetail.PollInterval * 3);
        await SettleAsync();

        Assert.Equal(pollsAtDispose, PollCount());
    }

    [Fact]
    public void Polling_FailedRefresh_KeepsTheLastGoodState()
    {
        var cut = RenderPolling(
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)),
            Failure(),
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Releasing)));

        Time.Advance(TransferDetail.PollInterval);
        cut.WaitForAssertion(() => Assert.Equal(2, PollCount()));

        Assert.Equal("Approved", StateText(cut));

        Time.Advance(TransferDetail.PollInterval);
        cut.WaitForAssertion(() => Assert.Equal("Moving items", StateText(cut)));
    }

    [Fact]
    public void Polling_ThrownException_KeepsTheLastGoodStateAndLogsTheExceptionType()
    {
        var cut = RenderPolling(
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Approved)),
            Task.FromException<ApiResponse<TransferDto>>(new HttpRequestException("offline")),
            Respond(Transfer(_transferId, Guid.NewGuid(), TransferStateDto.Releasing)));

        Time.Advance(TransferDetail.PollInterval);

        cut.WaitForAssertion(() => Assert.Contains("Polling a transfer failed with HttpRequestException", LogText, StringComparison.Ordinal));
        Assert.Equal("Approved", StateText(cut));
    }
}
