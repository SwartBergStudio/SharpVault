// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using NSubstitute;
using SharpVault.Dtos.OneTimeLinks;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.OneTimeLinks;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.OneTimeLinks;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinksViewTests : ShellTestContext
{
    private const string FirstName = "Bank login";
    private const string SecondName = "Mail password";
    private const string Token = "0123456789abcdef0123456789abcdef";
    private const string TableId = "#one-time-links-table";

    private readonly OneTimeLinkDto _first = Link(FirstName);
    private readonly OneTimeLinkDto _second = Link(SecondName);

    [Fact]
    public void Load_Empty_RendersTheEmptyState()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [] });

        var cut = Render<OneTimeLinksView>();

        cut.WaitForAssertion(() => Assert.Contains("No one-time links", cut.Markup, StringComparison.Ordinal));
    }

    [Fact]
    public void Load_Rows_RendersTheLinks()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [_first, _second] });

        var cut = Render<OneTimeLinksView>();

        var table = cut.WaitForElement(TableId);
        Assert.Contains(FirstName, table.TextContent, StringComparison.Ordinal);
        Assert.Contains(SecondName, table.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Revoke_Confirmed_RemovesTheRow()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [_first, _second] });
        OneTimeLinksClient.RevokeAsync(_first.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Success(true)));
        var cut = Render<OneTimeLinksView>();
        cut.WaitForElement("#one-time-link-revoke-" + _first.Id.ToString("D")).Click();

        cut.InvokeAsync(() => cut.FindComponent<ConfirmDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() =>
        {
            Assert.DoesNotContain(FirstName, cut.Find(TableId).TextContent, StringComparison.Ordinal);
            Assert.Contains(OneTimeLinksView.RevokedText, cut.Markup, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Revoke_NotFound_RemovesTheRow()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [_first, _second] });
        OneTimeLinksClient.RevokeAsync(_first.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.NotFound()));
        var cut = Render<OneTimeLinksView>();
        cut.WaitForElement("#one-time-link-revoke-" + _first.Id.ToString("D")).Click();

        cut.InvokeAsync(() => cut.FindComponent<ConfirmDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() => Assert.DoesNotContain(FirstName, cut.Find(TableId).TextContent, StringComparison.Ordinal));
    }

    [Fact]
    public void Revoke_Fails_KeepsTheRowAndShowsAnError()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [_first, _second] });
        OneTimeLinksClient.RevokeAsync(_first.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<bool>.Failed()));
        var cut = Render<OneTimeLinksView>();
        cut.WaitForElement("#one-time-link-revoke-" + _first.Id.ToString("D")).Click();

        cut.InvokeAsync(() => cut.FindComponent<ConfirmDialog>().Instance.OnConfirm.InvokeAsync());

        cut.WaitForAssertion(() => _ = OneTimeLinksClient.Received(1).RevokeAsync(_first.Id, Arg.Any<CancellationToken>()));
        Assert.Contains(FirstName, cut.Find(TableId).TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(OneTimeLinksView.RevokedText, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void LoadMore_PassesTheToken()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [_first], ContinuationToken = Token });
        OneTimeLinksClient.ListAsync(OneTimeLinkUiLimits.PageSize, Token, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Success(new OneTimeLinkPageDto { Items = [_second] })));
        var cut = Render<OneTimeLinksView>();
        cut.WaitForElement("#one-time-links-load-more").Click();

        cut.WaitForAssertion(() => OneTimeLinksClient.Received(1).ListAsync(OneTimeLinkUiLimits.PageSize, Token, Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Row_NullName_ShowsUnreadable()
    {
        ReturnsPage(new OneTimeLinkPageDto { Items = [Link(null)] });

        var cut = Render<OneTimeLinksView>();

        Assert.Contains(OneTimeLinkText.UnreadableNameText, cut.WaitForElement(TableId).TextContent, StringComparison.Ordinal);
    }

    private void ReturnsPage(OneTimeLinkPageDto page) =>
        OneTimeLinksClient.ListAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiResponse<OneTimeLinkPageDto>.Success(page)));

    private static OneTimeLinkDto Link(string? name) => new()
    {
        Id = Guid.NewGuid(),
        ItemName = name,
        CreatedAt = DateTimeOffset.UtcNow,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
    };
}
