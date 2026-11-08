// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvDialogTests : MudTestContext
{
    private const string Prefix = "delete-secret";
    private const string FooterButtons =
        "<button id=\"delete-secret-cancel\" type=\"button\">Cancel</button><button id=\"delete-secret-confirm\" type=\"button\">Delete</button>";

    private readonly IRenderedComponent<MudDialogProvider> _provider;

    public SvDialogTests()
    {
        _provider = RenderDialogProviders();
    }

    private Task PressKeyAsync(string key) => _provider.Find("#dialog-body").KeyDownAsync(new KeyboardEventArgs { Key = key });

    private IRenderedComponent<SvDialog> RenderDialog(bool open, int[]? cancels = null, bool busy = false) =>
        Render<SvDialog>(parameters =>
        {
            parameters
                .Add(dialog => dialog.IdPrefix, Prefix)
                .Add(dialog => dialog.Title, "Delete secret")
                .Add(dialog => dialog.Open, open)
                .Add(dialog => dialog.Busy, busy)
                .Add(dialog => dialog.Footer, FooterButtons)
                .AddChildContent("<p id=\"dialog-body\">Body</p>");
            if (cancels is not null)
            {
                parameters.Add(dialog => dialog.OnCancel, () => cancels[0]++);
            }
        });

    [Fact]
    [Trait("Category", "Security")]
    public void Dialog_Closed_RendersNothing()
    {
        RenderDialog(open: false);

        Assert.Empty(_provider.FindAll("button"));
        Assert.Empty(_provider.FindAll("[role=dialog]"));
        Assert.DoesNotContain("Delete secret", _provider.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dialog_Escape_RaisesCancel()
    {
        var cancels = new[] { 0 };
        RenderDialog(open: true, cancels: cancels);

        await PressKeyAsync("Escape");

        Assert.Equal(1, cancels[0]);
    }

    [Fact]
    public async Task Dialog_BusyEscape_DoesNotRaiseCancelAndSameDialogStaysShown()
    {
        var cancels = new[] { 0 };
        RenderDialog(open: true, cancels: cancels, busy: true);
        var before = _provider.FindComponent<MudDialog>().Instance;

        await PressKeyAsync("Escape");

        Assert.Equal(0, cancels[0]);
        Assert.Same(before, _provider.FindComponent<MudDialog>().Instance);
        Assert.NotEmpty(_provider.FindAll("[role=dialog]"));
    }

    [Fact]
    public void Dialog_BackdropClick_NeverRaisesCancel()
    {
        var cancels = new[] { 0 };
        RenderDialog(open: true, cancels: cancels);

        _provider.Find(".mud-overlay").Click();

        Assert.Equal(0, cancels[0]);
        Assert.NotEmpty(_provider.FindAll("[role=dialog]"));
    }

    [Fact]
    public void Dialog_Open_FocusesTheContentWrapperOnce()
    {
        RenderDialog(open: true);

        Assert.Single(WrapperFocusTargets());
    }

    [Fact]
    public async Task Dialog_ReopenedAfterClose_FocusesTheWrapperAgain()
    {
        var dialog = RenderDialog(open: true);

        dialog.Render(parameters => parameters.Add(component => component.Open, false));
        await WaitForClosedDialogsToSettleAsync();
        dialog.Render(parameters => parameters.Add(component => component.Open, true));

        _provider.WaitForAssertion(() => Assert.Contains("sv-dialog__body", LastFocusTarget().ClassList));
    }

    private List<string> FocusRequestIds() =>
        JSInterop.Invocations
            .Where(invocation => invocation.Identifier == "Blazor._internal.domWrapper.focus")
            .Select(invocation => Assert.IsType<ElementReference>(invocation.Arguments[0]).Id)
            .ToList();

    private List<AngleSharp.Dom.IElement> FocusTargets()
    {
        var dialogElements = _provider.Find("[role=dialog]").QuerySelectorAll("*");
        return FocusRequestIds()
            .Select(referenceId => Assert.Single(dialogElements, element => element.GetAttribute("blazor:elementreference") == referenceId))
            .ToList();
    }

    private List<AngleSharp.Dom.IElement> WrapperFocusTargets() =>
        FocusTargets().Where(element => element.ClassList.Contains("sv-dialog__body")).ToList();

    private AngleSharp.Dom.IElement LastFocusTarget()
    {
        var referenceId = FocusRequestIds()[^1];
        return Assert.Single(_provider.Find("[role=dialog]").QuerySelectorAll("*"), element => element.GetAttribute("blazor:elementreference") == referenceId);
    }
}
