// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class DialogShellTests : BunitContext
{
    private const string Prefix = "delete-secret";
    private const string FooterButtons =
        "<button id=\"delete-secret-cancel\" type=\"button\">Cancel</button><button id=\"delete-secret-confirm\" type=\"button\">Delete</button>";

    public DialogShellTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogShell_Closed_RendersNothing()
    {
        var cut = Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "Delete secret")
            .Add(dialog => dialog.Open, false)
            .Add(dialog => dialog.Footer, FooterButtons)
            .AddChildContent("<p id=\"dialog-body\">Body</p>"));

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup), $"Closed dialog rendered: {cut.Markup}");
        Assert.Empty(cut.FindAll("button"));
    }

    [Fact]
    public void DialogShell_DefaultOpen_IsClosed()
    {
        var cut = Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "Delete secret"));

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup));
    }

    [Fact]
    public void DialogShell_Open_HasDialogRoleModalAndLabelledByTitle()
    {
        var cut = RenderOpen();

        var dialog = cut.Find($"#{Prefix}-dialog");
        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal($"{Prefix}-title", dialog.GetAttribute("aria-labelledby"));
        Assert.Equal("-1", dialog.GetAttribute("tabindex"));
        Assert.Equal("Delete secret", cut.Find($"#{Prefix}-title").TextContent.Trim());
    }

    [Fact]
    public void DialogShell_Open_IdsDerivedFromIdPrefix()
    {
        var cut = RenderOpen();

        Assert.NotNull(cut.Find($"#{Prefix}-backdrop"));
        Assert.NotNull(cut.Find($"#{Prefix}-backdrop > #{Prefix}-dialog"));
        Assert.NotNull(cut.Find($"#{Prefix}-dialog h2#{Prefix}-title"));
    }

    [Fact]
    public void DialogShell_Open_RendersBodyAndFooterInDomOrderWithCancelFirst()
    {
        var cut = RenderOpen();

        Assert.Equal("Body", cut.Find(".sv-dialog__body #dialog-body").TextContent);
        var buttons = cut.FindAll(".sv-dialog__footer button");
        Assert.Equal([$"{Prefix}-cancel", $"{Prefix}-confirm"], buttons.Select(button => button.Id));
    }

    [Fact]
    public void DialogShell_WithoutFooter_RendersNoFooterElement()
    {
        var cut = Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "T")
            .Add(dialog => dialog.Open, true));

        Assert.Empty(cut.FindAll("footer"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogShell_Opened_FocusesDialogNotAButton()
    {
        var cut = Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "Delete secret")
            .Add(dialog => dialog.Open, false)
            .Add(dialog => dialog.Footer, FooterButtons));

        cut.Render(parameters => parameters.Add(dialog => dialog.Open, true));

        var invocation = JSInterop.VerifyFocusAsyncInvoke();
        invocation.Arguments[0].ShouldBeElementReferenceTo(cut.Find($"#{Prefix}-dialog"));
        Assert.Single(JSInterop.Invocations, call => call.Identifier == invocation.Identifier);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogShell_RenderedOpenFromTheStart_FocusesDialogNotAButton()
    {
        var cut = RenderOpen();

        var invocation = JSInterop.VerifyFocusAsyncInvoke();
        invocation.Arguments[0].ShouldBeElementReferenceTo(cut.Find($"#{Prefix}-dialog"));
    }

    [Fact]
    public void DialogShell_RerenderedWhileOpen_DoesNotStealFocusAgain()
    {
        var cut = RenderOpen();
        var identifier = JSInterop.VerifyFocusAsyncInvoke().Identifier;
        var before = JSInterop.Invocations.Count(call => call.Identifier == identifier);

        cut.Render(parameters => parameters.Add(dialog => dialog.Title, "Another title"));

        var after = JSInterop.Invocations.Count(call => call.Identifier == identifier);
        Assert.Equal(before, after);
    }

    [Fact]
    public void DialogShell_Closed_DoesNotFocusAnything()
    {
        Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "T")
            .Add(dialog => dialog.Open, false));

        Assert.Empty(JSInterop.Invocations);
    }

    [Fact]
    public void DialogShell_EscapePressed_InvokesOnCancel()
    {
        var cancelled = 0;
        var cut = RenderOpen(EventCallback.Factory.Create(this, () => cancelled++));

        cut.Find($"#{Prefix}-dialog").KeyDown(new KeyboardEventArgs { Key = "Escape" });
        Assert.Equal(1, cancelled);

        cut.Find($"#{Prefix}-dialog").KeyDown(new KeyboardEventArgs { Key = "Enter" });
        cut.Find($"#{Prefix}-dialog").KeyDown(new KeyboardEventArgs { Key = " " });
        cut.Find($"#{Prefix}-dialog").KeyDown(new KeyboardEventArgs { Key = "Tab" });
        Assert.Equal(1, cancelled);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogShell_BackdropClicked_HasNoHandlerSoNothingCancels()
    {
        var cancelled = 0;
        var cut = RenderOpen(EventCallback.Factory.Create(this, () => cancelled++));
        var backdrop = cut.Find($"#{Prefix}-backdrop");

        Assert.DoesNotContain(backdrop.Attributes, attribute => attribute.Name.StartsWith("blazor:on", StringComparison.Ordinal));
        Assert.Throws<MissingEventHandlerException>(() => backdrop.Click());
        Assert.Equal(0, cancelled);
        Assert.NotNull(cut.Find($"#{Prefix}-dialog"));
    }

    [Fact]
    public void DialogShell_Danger_AddsDangerModifierAndIcon()
    {
        var plain = RenderOpen();
        var danger = Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, Prefix)
            .Add(dialog => dialog.Title, "Delete secret")
            .Add(dialog => dialog.Open, true)
            .Add(dialog => dialog.Danger, true));

        Assert.Contains("sv-dialog--danger", danger.Find($"#{Prefix}-dialog").ClassList);
        Assert.NotNull(danger.Find($"#{Prefix}-title svg.sv-icon"));
        Assert.DoesNotContain("sv-dialog--danger", plain.Find($"#{Prefix}-dialog").ClassList);
        Assert.Empty(plain.FindAll($"#{Prefix}-title svg"));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("1x")]
    [InlineData("a/b")]
    public void DialogShell_InvalidIdPrefix_Throws(string? prefix)
    {
        ThrownExceptions.ArgumentFrom(() => Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, prefix!)
            .Add(dialog => dialog.Title, "T")
            .Add(dialog => dialog.Open, true)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogShell_ClosedWithInvalidIdPrefix_StillThrows()
    {
        ThrownExceptions.ArgumentFrom(() => Render<DialogShell>(parameters => parameters
            .Add(dialog => dialog.IdPrefix, "Bad Prefix")
            .Add(dialog => dialog.Title, "T")
            .Add(dialog => dialog.Open, false)));
    }

    private IRenderedComponent<DialogShell> RenderOpen(EventCallback? onCancel = null) =>
        Render<DialogShell>(parameters =>
        {
            parameters
                .Add(dialog => dialog.IdPrefix, Prefix)
                .Add(dialog => dialog.Title, "Delete secret")
                .Add(dialog => dialog.Open, true)
                .Add(dialog => dialog.Footer, FooterButtons)
                .AddChildContent("<p id=\"dialog-body\">Body</p>");
            if (onCancel is { } callback)
            {
                parameters.Add(dialog => dialog.OnCancel, callback);
            }
        });
}
