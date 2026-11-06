// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Web;
using NSubstitute;
using SharpVault.Dtos.Transfers;
using SharpVault.UI.Shared.Components.Transfers;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Transfers;

public sealed class TransferListTests : TransferUiTestHost
{
    private const string MoreToken = "page-two";

    private readonly Guid _firstId = Guid.NewGuid();

    private readonly Guid _secondId = Guid.NewGuid();

    private IRenderedComponent<TransferList> RenderWithFirstPage(TransferPageDto page)
    {
        Transfers.ListAsync(TransferList.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPageDto>.Success(page)));
        return Render<TransferList>();
    }

    [Fact]
    public void Load_AdminWithRows_ShowsOneLinkAndStateForEachRow()
    {
        var cut = RenderWithFirstPage(new TransferPageDto
        {
            Items = [Summary(_firstId), Summary(_secondId, TransferStateDto.Completed)],
        });

        Assert.NotNull(cut.Find($"#transfer-link-{Id(_firstId)}"));
        Assert.NotNull(cut.Find($"#transfer-link-{Id(_secondId)}"));
        Assert.Contains(TransferStatusText.State(TransferStateDto.Completed), cut.Find($"#transfer-state-{Id(_secondId)}").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_RequestedTime_ShowsTheZoneLabel()
    {
        var cut = RenderWithFirstPage(new TransferPageDto { Items = [Summary(_firstId)] });

        Assert.Contains("2026-03-01 09:00 UTC", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Rows_LinkToTheDetailRoute()
    {
        var cut = RenderWithFirstPage(new TransferPageDto { Items = [Summary(_firstId)] });

        Assert.EndsWith($"/admin/transfers/{Id(_firstId)}", cut.Find($"#transfer-link-{Id(_firstId)}").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Load_NonAdmin_ShowsForbiddenAndCallsNoListApi()
    {
        SignInAsNonAdmin();

        var cut = Render<TransferList>();

        Assert.NotNull(cut.Find("#transfers-forbidden"));
        Assert.Empty(cut.FindAll("#transfers-table"));
        _ = Transfers.DidNotReceiveWithAnyArgs().ListAsync(default, default, Xunit.TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Empty_ShowsTheEmptyState()
    {
        var cut = RenderWithFirstPage(new TransferPageDto());

        Assert.NotNull(cut.Find("#transfers-empty"));
        Assert.Empty(cut.FindAll("#transfers-table"));
    }

    [Fact]
    public void LoadFailed_ShowsTheErrorWithRetry()
    {
        Transfers.ListAsync(TransferList.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPageDto>.Failed()));

        var cut = Render<TransferList>();

        Assert.NotNull(cut.Find("#transfers-error"));
        Assert.NotNull(cut.Find("#transfers-retry"));
    }

    [Fact]
    public async Task LoadMore_Success_AppendsTheNextPage()
    {
        var cut = RenderWithFirstPage(new TransferPageDto { Items = [Summary(_firstId)], ContinuationToken = MoreToken });
        Transfers.ListAsync(TransferList.PageSize, MoreToken, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPageDto>.Success(new TransferPageDto { Items = [Summary(_secondId)] })));

        await cut.Find("#transfers-load-more").ClickAsync(new MouseEventArgs());

        Assert.NotNull(cut.Find($"#transfer-link-{Id(_secondId)}"));
        Assert.NotNull(cut.Find($"#transfer-link-{Id(_firstId)}"));
        Assert.Empty(cut.FindAll("#transfers-load-more-error"));
    }

    [Fact]
    public async Task LoadMore_Failed_KeepsTheRowsAndShowsTheInlineError()
    {
        var cut = RenderWithFirstPage(new TransferPageDto { Items = [Summary(_firstId)], ContinuationToken = MoreToken });
        Transfers.ListAsync(TransferList.PageSize, MoreToken, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPageDto>.Failed()));

        await cut.Find("#transfers-load-more").ClickAsync(new MouseEventArgs());

        Assert.NotNull(cut.Find($"#transfer-link-{Id(_firstId)}"));
        Assert.NotNull(cut.Find("#transfers-load-more-error"));
        Assert.NotNull(cut.Find("#transfers-load-more"));
    }

    [Fact]
    public void Route_AdminTransfers_RendersTheListPage()
    {
        Transfers.ListAsync(TransferList.PageSize, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<TransferPageDto>.Success(new TransferPageDto { Items = [Summary(_firstId)] })));

        var cut = RenderRouter(TransferRoutes.Transfers);

        cut.WaitForElement("#transfers-table");
        Assert.Empty(cut.FindAll("#route-not-found"));
    }
}
