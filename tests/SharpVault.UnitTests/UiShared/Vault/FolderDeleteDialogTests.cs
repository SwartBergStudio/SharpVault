// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using MudBlazor;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Components.Vault;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class FolderDeleteDialogTests : MudTestContext
{
    private readonly IRenderedComponent<MudDialogProvider> _provider;
    private readonly List<FolderDeleteModeDto> _confirmed = [];
    private int _cancelled;

    public FolderDeleteDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private IRenderedComponent<FolderDeleteDialog> RenderDialog(
        bool open = true,
        string name = "Finance",
        bool busy = false,
        string? errorText = null) =>
        Render<FolderDeleteDialog>(parameters =>
        {
            parameters
                .Add(dialog => dialog.Open, open)
                .Add(dialog => dialog.FolderName, name)
                .Add(dialog => dialog.Busy, busy)
                .Add(dialog => dialog.OnConfirm, mode => _confirmed.Add(mode))
                .Add(dialog => dialog.OnCancel, () => _cancelled++);
            if (errorText is not null)
            {
                parameters.Add(dialog => dialog.ErrorText, errorText);
            }
        });

    private bool ConfirmDisabled() => _provider.Find("#folder-delete-confirm").HasAttribute("disabled");

    private void Choose(string id) => _provider.Find($"#{id}").Click();

    [Fact]
    public void Closed_RendersNothing()
    {
        RenderDialog(open: false);

        Assert.Empty(_provider.FindAll("#folder-delete-dialog"));
        Assert.Empty(_provider.FindAll("#folder-delete-confirm"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Initially_NoModeSelectedAndConfirmDisabled()
    {
        RenderDialog();

        Assert.True(ConfirmDisabled());
        Assert.Empty(_provider.FindAll("input[type=radio]:checked"));
        Assert.Equal(2, _provider.FindAll("input[type=radio]").Count);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Confirm_ClickedWithNoModeChosen_RaisesNothing()
    {
        RenderDialog();

        _provider.Find("#folder-delete-confirm").Click();

        Assert.Empty(_confirmed);
    }

    [Fact]
    public void ModeIds_LandOnAnElementATestCanClick()
    {
        RenderDialog();

        var move = _provider.Find("#folder-delete-mode-move");
        var delete = _provider.Find("#folder-delete-mode-delete");

        Assert.NotNull(move);
        Assert.NotNull(delete);
        Assert.NotSame(move, delete);
        Assert.Single(_provider.FindAll("#folder-delete-mode-move"));
        Assert.Single(_provider.FindAll("#folder-delete-mode-delete"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SelectMove_EnablesConfirmAndSendsMoveToParent()
    {
        RenderDialog();

        Choose("folder-delete-mode-move");

        _provider.WaitForAssertion(() => Assert.False(ConfirmDisabled()));
        _provider.Find("#folder-delete-confirm").Click();
        Assert.Equal([FolderDeleteModeDto.MoveToParent], _confirmed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SelectDelete_SendsDeleteContents()
    {
        RenderDialog();

        Choose("folder-delete-mode-delete");

        _provider.WaitForAssertion(() => Assert.False(ConfirmDisabled()));
        _provider.Find("#folder-delete-confirm").Click();
        Assert.Equal([FolderDeleteModeDto.DeleteContents], _confirmed);
    }

    [Fact]
    public void SwitchingMode_SendsTheLastChoice()
    {
        RenderDialog();
        Choose("folder-delete-mode-delete");
        _provider.WaitForAssertion(() => Assert.False(ConfirmDisabled()));

        Choose("folder-delete-mode-move");
        _provider.Find("#folder-delete-confirm").Click();

        Assert.Equal([FolderDeleteModeDto.MoveToParent], _confirmed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Reopen_AfterChoosingDelete_ResetsToNoSelectionAndConfirmDisabled()
    {
        var cut = RenderDialog();
        Choose("folder-delete-mode-delete");
        _provider.WaitForAssertion(() => Assert.False(ConfirmDisabled()));

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, false));
        _provider.WaitForAssertion(() => Assert.Empty(_provider.FindAll("#folder-delete-confirm")));
        await WaitForClosedDialogsToSettleAsync();
        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));

        _provider.WaitForElement("#folder-delete-confirm");
        Assert.True(ConfirmDisabled());
        Assert.Empty(_provider.FindAll("input[type=radio]:checked"));
        await _provider.Find("#folder-delete-confirm").ClickAsync();
        Assert.Empty(_confirmed);
    }

    [Fact]
    public void Busy_DisablesConfirmAndCancelAndIgnoresClicks()
    {
        var cut = RenderDialog();
        Choose("folder-delete-mode-move");
        _provider.WaitForAssertion(() => Assert.False(ConfirmDisabled()));

        cut.Render(parameters => parameters.Add(dialog => dialog.Busy, true));

        _provider.WaitForAssertion(() => Assert.True(ConfirmDisabled()));
        Assert.True(_provider.Find("#folder-delete-cancel").HasAttribute("disabled"));
    }

    [Fact]
    public void Cancel_RaisesOnCancelOnly()
    {
        RenderDialog();

        _provider.Find("#folder-delete-cancel").Click();

        Assert.Equal(1, _cancelled);
        Assert.Empty(_confirmed);
    }

    [Fact]
    public void ErrorText_IsShown()
    {
        RenderDialog(errorText: "That did not work. Try again.");

        var error = _provider.Find("#folder-delete-error");
        Assert.Equal("alert", error.GetAttribute("role"));
        Assert.Contains("That did not work. Try again.", error.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void NoErrorText_ShowsNoAlert()
    {
        RenderDialog();

        Assert.Empty(_provider.FindAll("#folder-delete-error"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void FolderNameWithMarkup_RendersEncoded()
    {
        RenderDialog(name: "<img src=x onerror=alert(1)>");

        Assert.Empty(_provider.FindAll("img"));
        Assert.Empty(_provider.FindAll("[onerror]"));
        Assert.Contains("&lt;img", _provider.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void Dialog_IsLabelled()
    {
        RenderDialog();

        var dialog = _provider.Find("[role=dialog]");
        Assert.Equal("folder-delete-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Contains("Delete folder", _provider.Find("#folder-delete-title").TextContent, StringComparison.Ordinal);
    }
}
