// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferNoticeBannerTests : TransferUiTestHost
{
    private readonly Guid _receivedId = Guid.NewGuid();

    private readonly Guid _completedId = Guid.NewGuid();

    private static TransferNoticeDto Notice(Guid transferId, TransferNoticeKindDto kind) => new()
    {
        TransferId = transferId,
        Kind = kind,
        SourceDisplayName = "User One",
        CompletedAt = At,
        ItemCount = 3,
    };

    private IRenderedComponent<TransferNoticeBanner> RenderWithNotices(params TransferNoticeDto[] notices)
    {
        TransferNoticesClient.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferNoticeListDto>.Success(new TransferNoticeListDto { Items = [.. notices] })));
        return Render<TransferNoticeBanner>();
    }

    [Fact]
    public void NoNotices_RendersNothing()
    {
        var cut = RenderWithNotices();

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void LoadFails_RendersNothing()
    {
        TransferNoticesClient.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferNoticeListDto>.Failed()));

        var cut = Render<TransferNoticeBanner>();

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void ReceivedNotice_OffersOpenAndReviewShares()
    {
        var cut = RenderWithNotices(Notice(_receivedId, TransferNoticeKindDto.Received));

        Assert.NotNull(cut.Find($"#transfer-notice-open-{Id(_receivedId)}"));
        Assert.NotNull(cut.Find($"#transfer-notice-shares-{Id(_receivedId)}"));
    }

    [Fact]
    public void CompletedNoticeForTheRequester_OffersNoShareReview()
    {
        var cut = RenderWithNotices(Notice(_completedId, TransferNoticeKindDto.RequestCompleted));

        Assert.NotNull(cut.Find($"#transfer-notice-open-{Id(_completedId)}"));
        Assert.Empty(cut.FindAll($"#transfer-notice-shares-{Id(_completedId)}"));
    }

    [Fact]
    public async Task Dismiss_CallsTheApiAndRemovesTheRow()
    {
        TransferNoticesClient.DismissAsync(_receivedId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = RenderWithNotices(Notice(_receivedId, TransferNoticeKindDto.Received));

        await cut.Find($"#transfer-notice-dismiss-{Id(_receivedId)}").ClickAsync(new MouseEventArgs());

        _ = TransferNoticesClient.Received(1).DismissAsync(_receivedId, Arg.Any<CancellationToken>());
        Assert.Empty(cut.FindAll($"#transfer-notice-{Id(_receivedId)}"));
    }

    [Fact]
    public async Task DismissFails_KeepsTheRowAndShowsTheStatus()
    {
        TransferNoticesClient.DismissAsync(_receivedId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Failed()));
        var cut = RenderWithNotices(Notice(_receivedId, TransferNoticeKindDto.Received));

        await cut.Find($"#transfer-notice-dismiss-{Id(_receivedId)}").ClickAsync(new MouseEventArgs());

        Assert.NotNull(cut.Find($"#transfer-notice-{Id(_receivedId)}"));
        Assert.NotEmpty(cut.Find("#transfer-notice-status").TextContent.Trim());
    }
}
