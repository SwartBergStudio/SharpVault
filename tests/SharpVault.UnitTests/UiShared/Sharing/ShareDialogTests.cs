// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Components.Sharing;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class ShareDialogTests : SharingDialogTestHost
{
    private readonly List<ShareDialogInput> _confirmed = [];
    private readonly List<string> _terms = [];
    private int _cancelled;

    private readonly DirectoryUserDto _found = new() { Id = Guid.NewGuid(), DisplayName = "Grace Hopper", Email = "grace@example.test" };

    private IRenderedComponent<ShareDialog> RenderDialog(bool open = true, bool busy = false, string? error = null, bool canGrantManage = true) =>
        Render<ShareDialog>(parameters => parameters
            .Add(dialog => dialog.Open, open)
            .Add(dialog => dialog.Busy, busy)
            .Add(dialog => dialog.ErrorText, error)
            .Add(dialog => dialog.CanGrantManage, canGrantManage)
            .Add(dialog => dialog.Search, (term, _) =>
            {
                _terms.Add(term);
                return Task.FromResult(ApiResponse<DirectoryUserListDto>.Success(new DirectoryUserListDto { Items = [_found] }));
            })
            .Add(dialog => dialog.OnConfirm, input => _confirmed.Add(input))
            .Add(dialog => dialog.OnCancel, () => _cancelled++));

    private async Task PickAsync()
    {
        Provider.WaitForElement("#recipient-search-input").Input("gra");
        await Provider.Find("#recipient-search-form").SubmitAsync();
        await Provider.WaitForElement($"#recipient-result-{_found.Id:D}").ClickAsync();
        Provider.WaitForElement("#recipient-selected");
    }

    [Fact]
    public void Render_Closed_ShowsNothing()
    {
        RenderDialog(open: false);

        Assert.Empty(Provider.FindAll("#share-dialog-confirm"));
        Assert.Empty(Provider.FindAll("#recipient-search-input"));
    }

    [Fact]
    public void Render_Open_ShowsPickerPermissionAndFooterWithShareDisabled()
    {
        RenderDialog();

        Provider.WaitForElement("#share-dialog-confirm");
        Assert.NotEmpty(Provider.FindAll("#recipient-search-input"));
        Assert.Equal(nameof(SharePermissionDto.View), Provider.Find("#share-dialog-permission").GetAttribute("value"));
        Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
        Assert.False(Provider.Find("#share-dialog-cancel").HasAttribute("disabled"));
        Assert.Empty(Provider.FindAll("#share-dialog-error"));
    }

    [Fact]
    public async Task Confirm_WithoutARecipient_RaisesNothing()
    {
        RenderDialog();
        var confirm = Provider.WaitForElement("#share-dialog-confirm");

        await confirm.ClickAsync();

        Assert.Empty(_confirmed);
    }

    [Fact]
    public async Task Confirm_AfterPicking_RaisesTheRecipientWithViewByDefault()
    {
        RenderDialog();
        await PickAsync();

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        var input = Assert.Single(_confirmed);
        Assert.Equal(_found, input.Recipient);
        Assert.Equal(SharePermissionDto.View, input.Permission);
        Assert.Equal(["gra"], _terms);
    }

    [Theory]
    [InlineData(SharePermissionDto.Edit)]
    [InlineData(SharePermissionDto.Manage)]
    public async Task Confirm_AfterChoosingAPermission_RaisesThatPermission(SharePermissionDto permission)
    {
        RenderDialog();
        await PickAsync();
        Provider.Find("#share-dialog-permission").Change(permission.ToString());

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        Assert.Equal(permission, Assert.Single(_confirmed).Permission);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("Owner")]
    [InlineData("")]
    public async Task Confirm_AfterAnUnknownPermissionValue_KeepsTheCurrentPermission(string value)
    {
        RenderDialog();
        await PickAsync();
        Provider.Find("#share-dialog-permission").Change(nameof(SharePermissionDto.Edit));
        Provider.Find("#share-dialog-permission").Change(value);

        await Provider.Find("#share-dialog-confirm").ClickAsync();

        Assert.Equal(SharePermissionDto.Edit, Assert.Single(_confirmed).Permission);
    }

    [Fact]
    public async Task Confirm_AfterClearingTheRecipient_RaisesNothingAndDisablesShare()
    {
        RenderDialog();
        await PickAsync();

        await Provider.Find("#recipient-clear").ClickAsync();

        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled")));
        await Provider.Find("#share-dialog-confirm").ClickAsync();
        Assert.Empty(_confirmed);
    }

    [Fact]
    public async Task Cancel_RaisesOnCancelOnly()
    {
        RenderDialog();

        await Provider.WaitForElement("#share-dialog-cancel").ClickAsync();

        Assert.Equal(1, _cancelled);
        Assert.Empty(_confirmed);
    }

    [Fact]
    public async Task Busy_DisablesEveryControlAndIgnoresConfirmAndCancel()
    {
        var cut = RenderDialog();
        await PickAsync();

        cut.Render(parameters => parameters.Add(dialog => dialog.Busy, true));

        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-dialog-cancel").HasAttribute("disabled")));
        Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
        Assert.True(Provider.Find("#share-dialog-permission").HasAttribute("disabled"));
        Assert.True(Provider.Find("#recipient-clear").HasAttribute("disabled"));
        await Provider.Find("#share-dialog-confirm").ClickAsync();
        await Provider.Find("#share-dialog-cancel").ClickAsync();
        Assert.Empty(_confirmed);
        Assert.Equal(0, _cancelled);
    }

    [Fact]
    public async Task Busy_EscapeDoesNotCancelAndTheDialogStaysOpen()
    {
        var cut = RenderDialog();
        await PickAsync();
        cut.Render(parameters => parameters.Add(dialog => dialog.Busy, true));
        Provider.WaitForAssertion(() => Assert.True(Provider.Find("#share-dialog-cancel").HasAttribute("disabled")));

        await PressEscapeAsync();
        await PollAsync(() => Provider.FindAll("#share-dialog-confirm").Count > 0);

        Assert.Equal(0, _cancelled);
        Assert.NotEmpty(Provider.FindAll("#share-dialog-confirm"));
    }

    [Fact]
    public async Task Idle_EscapeCancels()
    {
        RenderDialog();
        Provider.WaitForElement("#share-dialog-confirm");

        await PressEscapeAsync();

        await PollAsync(() => _cancelled > 0);

        Assert.Equal(1, _cancelled);
    }

    [Fact]
    public void Render_ErrorText_ShowsTheAlertEncoded()
    {
        RenderDialog(error: "<b>Not</b> allowed");

        var alert = Provider.WaitForElement("#share-dialog-error");

        Assert.Contains("<b>Not</b> allowed", alert.TextContent, StringComparison.Ordinal);
        Assert.Empty(alert.QuerySelectorAll("b"));
    }

    [Fact]
    public async Task Reopen_ResetsTheRecipientAndPermission()
    {
        var cut = RenderDialog();
        await PickAsync();
        Provider.Find("#share-dialog-permission").Change(nameof(SharePermissionDto.Manage));
        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));
        Provider.WaitForAssertion(() => Assert.Empty(Provider.FindAll("#share-dialog-confirm")));
        await WaitForClosedDialogsToSettleAsync();

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));

        Provider.WaitForElement("#share-dialog-confirm");
        Assert.Empty(Provider.FindAll("#recipient-selected"));
        Assert.Equal(nameof(SharePermissionDto.View), Provider.Find("#share-dialog-permission").GetAttribute("value"));
        Assert.True(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
        await Provider.Find("#share-dialog-confirm").ClickAsync();
        Assert.Empty(_confirmed);
    }

    [Fact]
    public async Task Rerender_WhileOpen_KeepsTheChosenRecipient()
    {
        var cut = RenderDialog();
        await PickAsync();

        cut.Render(parameters => parameters.Add(dialog => dialog.ErrorText, "Try again."));

        Provider.WaitForElement("#share-dialog-error");
        Assert.NotEmpty(Provider.FindAll("#recipient-selected"));
        Assert.False(Provider.Find("#share-dialog-confirm").HasAttribute("disabled"));
    }
}
