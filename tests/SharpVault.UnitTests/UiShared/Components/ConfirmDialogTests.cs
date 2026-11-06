// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using MudBlazor;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class ConfirmDialogTests : MudTestContext
{
    private const string Prefix = "delete-folder";

    private readonly IRenderedComponent<MudDialogProvider> _provider;

    public ConfirmDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private void RenderDialog(Action<ComponentParameterCollectionBuilder<ConfirmDialog>>? configure = null) =>
        Render<ConfirmDialog>(parameters =>
        {
            parameters
                .Add(dialog => dialog.IdPrefix, Prefix)
                .Add(dialog => dialog.Title, "Delete folder")
                .Add(dialog => dialog.Open, true)
                .AddChildContent("<p id=\"confirm-body\">Are you sure?</p>");
            configure?.Invoke(parameters);
        });

    [Fact]
    public void Confirm_RaisesOnConfirm()
    {
        var confirms = 0;
        var cancels = 0;
        RenderDialog(parameters => parameters
            .Add(dialog => dialog.OnConfirm, () => confirms++)
            .Add(dialog => dialog.OnCancel, () => cancels++));

        _provider.Find($"#{Prefix}-confirm").Click();

        Assert.Equal(1, confirms);
        Assert.Equal(0, cancels);
    }

    [Fact]
    public void Cancel_RaisesOnCancel()
    {
        var confirms = 0;
        var cancels = 0;
        RenderDialog(parameters => parameters
            .Add(dialog => dialog.OnConfirm, () => confirms++)
            .Add(dialog => dialog.OnCancel, () => cancels++));

        _provider.Find($"#{Prefix}-cancel").Click();

        Assert.Equal(0, confirms);
        Assert.Equal(1, cancels);
    }

    [Fact]
    public void Busy_DisablesConfirm()
    {
        var confirms = 0;
        RenderDialog(parameters => parameters
            .Add(dialog => dialog.Busy, true)
            .Add(dialog => dialog.OnConfirm, () => confirms++));

        Assert.True(_provider.Find($"#{Prefix}-confirm").HasAttribute("disabled"));
        Assert.True(_provider.Find($"#{Prefix}-cancel").HasAttribute("disabled"));
        Assert.Equal(0, confirms);
    }

    [Fact]
    public void Render_CancelComesBeforeConfirm()
    {
        RenderDialog();

        var footerButtonIds = _provider.FindAll("button")
            .Select(button => button.Id)
            .Where(id => id is $"{Prefix}-cancel" or $"{Prefix}-confirm");

        Assert.Equal([$"{Prefix}-cancel", $"{Prefix}-confirm"], footerButtonIds);
    }
}
