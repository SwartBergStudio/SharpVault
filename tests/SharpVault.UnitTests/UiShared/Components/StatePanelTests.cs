// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UI.Shared.Components.Icons;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class StatePanelTests : MudTestContext
{
    [Theory]
    [InlineData(StatePanelKind.Neutral, "status")]
    [InlineData(StatePanelKind.Warning, "alert")]
    [InlineData(StatePanelKind.Danger, "alert")]
    public void Kind_Always_MapsToRole(StatePanelKind kind, string role)
    {
        var cut = Render<StatePanel>(parameters => parameters
            .Add(panel => panel.Id, "panel-one")
            .Add(panel => panel.Title, "Title")
            .Add(panel => panel.Kind, kind));

        Assert.Equal(role, cut.Find("#panel-one").GetAttribute("role"));
    }

    [Fact]
    public void Title_Rendered_IsTheLabelOfThePanel()
    {
        var cut = Render<StatePanel>(parameters => parameters
            .Add(panel => panel.Id, "panel-one")
            .Add(panel => panel.Title, "Page not found"));

        Assert.Equal("panel-one-title", cut.Find("#panel-one").GetAttribute("aria-labelledby"));
        Assert.Equal("Page not found", cut.Find("#panel-one-title").TextContent);
    }

    [Fact]
    public void Title_WithMarkup_IsEncoded()
    {
        var cut = Render<StatePanel>(parameters => parameters
            .Add(panel => panel.Id, "panel-one")
            .Add(panel => panel.Title, "<b onclick=x>bold</b>"));

        Assert.Empty(cut.FindAll("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<StatePanel>(parameters => parameters
            .Add(panel => panel.Id, id)
            .Add(panel => panel.Title, "T")));
    }
}
