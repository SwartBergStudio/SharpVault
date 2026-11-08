// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Services;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkNoticeBannerTests : ShellTestContext
{
    private const string NoticeId = "n_0000000000000000001_0123456789abcdef0123456789abcdef";
    private const string NoticeAlert = "#one-time-link-notice-";
    private const string DismissButton = "#one-time-link-notice-dismiss-";

    private readonly Guid _linkId = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N");
    private readonly OneTimeLinkNoticeDto _notice = new()
    {
        NoticeId = NoticeId,
        LinkId = Guid.ParseExact("0123456789abcdef0123456789abcdef", "N"),
        ItemName = "Bank login",
        OpenedAt = new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.Zero),
    };

    [Fact]
    public void NoNotices_RendersNothing()
    {
        var cut = Render<OneTimeLinkNoticeBanner>();

        cut.WaitForAssertion(() => OneTimeLinksClient.Received(1).ListNoticesAsync(Arg.Any<CancellationToken>()));
        Assert.DoesNotContain(NoticeAlert, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Notice_RendersTheOpenedText()
    {
        ReturnsNotices(_notice);

        var cut = Render<OneTimeLinkNoticeBanner>();

        var alert = cut.WaitForElement(NoticeAlert + _linkId.ToString("D"));
        Assert.Contains(OneTimeLinkNoticeBanner.NoticeText(_notice), alert.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Dismiss_RemovesTheNotice()
    {
        ReturnsNotices(_notice);
        OneTimeLinksClient.DismissNoticeAsync(NoticeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = Render<OneTimeLinkNoticeBanner>();

        cut.WaitForElement(DismissButton + _linkId.ToString("D")).Click();

        cut.WaitForAssertion(() => Assert.Empty(cut.FindAll(NoticeAlert + _linkId.ToString("D"))));
        _ = OneTimeLinksClient.Received(1).DismissNoticeAsync(NoticeId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dismiss_Fails_KeepsTheNotice()
    {
        ReturnsNotices(_notice);
        OneTimeLinksClient.DismissNoticeAsync(NoticeId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Failed()));
        var cut = Render<OneTimeLinkNoticeBanner>();

        cut.WaitForElement(DismissButton + _linkId.ToString("D")).Click();

        cut.WaitForAssertion(() => _ = OneTimeLinksClient.Received(1).DismissNoticeAsync(NoticeId, Arg.Any<CancellationToken>()));
        Assert.NotEmpty(cut.FindAll(NoticeAlert + _linkId.ToString("D")));
    }

    private void ReturnsNotices(params OneTimeLinkNoticeDto[] notices)
    {
        var list = new OneTimeLinkNoticeListDto();
        list.Items.AddRange(notices);
        OneTimeLinksClient.ListNoticesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkNoticeListDto>.Success(list)));
    }
}
