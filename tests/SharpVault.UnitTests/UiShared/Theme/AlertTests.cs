// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class AlertTests : BunitContext
{
    [Theory]
    [InlineData(AlertKind.Danger)]
    [InlineData(AlertKind.Warning)]
    public void Alert_DangerOrWarning_UsesAlertRole(AlertKind kind)
    {
        var cut = Render<Alert>(parameters => parameters
            .Add(alert => alert.Id, "account-error")
            .Add(alert => alert.Kind, kind));

        Assert.Equal("alert", cut.Find("#account-error").GetAttribute("role"));
    }

    [Theory]
    [InlineData(AlertKind.Info)]
    [InlineData(AlertKind.Success)]
    public void Alert_InfoOrSuccess_UsesStatusRole(AlertKind kind)
    {
        var cut = Render<Alert>(parameters => parameters
            .Add(alert => alert.Id, "account-note")
            .Add(alert => alert.Kind, kind));

        Assert.Equal("status", cut.Find("#account-note").GetAttribute("role"));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(AlertKind.Info, "Information: ")]
    [InlineData(AlertKind.Success, "Success: ")]
    [InlineData(AlertKind.Warning, "Warning: ")]
    [InlineData(AlertKind.Danger, "Error: ")]
    public void Alert_EveryKind_RendersIconAndKindText(AlertKind kind, string prefix)
    {
        var cut = Render<Alert>(parameters => parameters
            .Add(alert => alert.Id, "alert-one")
            .Add(alert => alert.Kind, kind));

        Assert.Contains($"sv-alert--{kind.ToString().ToLowerInvariant()}", cut.Find("#alert-one").ClassList);
        Assert.NotNull(cut.Find("#alert-one").QuerySelector("svg.sv-icon.sv-alert__icon"));
        var hidden = cut.Find(".sv-alert__title .sv-visually-hidden");
        Assert.Equal(prefix, hidden.TextContent);
    }

    [Fact]
    public void Alert_DefaultKind_IsInfo()
    {
        var cut = Render<Alert>(parameters => parameters.Add(alert => alert.Id, "alert-default"));

        Assert.Contains("sv-alert--info", cut.Find("#alert-default").ClassList);
        Assert.Equal("status", cut.Find("#alert-default").GetAttribute("role"));
    }

    [Fact]
    public void Alert_TitleAndContent_AreRendered()
    {
        var cut = Render<Alert>(parameters => parameters
            .Add(alert => alert.Id, "alert-two")
            .Add(alert => alert.Kind, AlertKind.Danger)
            .Add(alert => alert.Title, "Could not load")
            .AddChildContent("<span id=\"detail\">Try again later.</span>"));

        Assert.Contains("Could not load", cut.Find(".sv-alert__title").TextContent, StringComparison.Ordinal);
        Assert.Equal("Try again later.", cut.Find("#detail").TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Alert_TitleWithMarkup_IsEncoded()
    {
        var cut = Render<Alert>(parameters => parameters
            .Add(alert => alert.Id, "alert-three")
            .Add(alert => alert.Title, "<b onclick=x>bold</b>"));

        Assert.Empty(cut.FindAll("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void Alert_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<Alert>(parameters => parameters.Add(alert => alert.Id, id)));
    }
}
