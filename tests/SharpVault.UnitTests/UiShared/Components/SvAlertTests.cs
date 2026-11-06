// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed class SvAlertTests : MudTestContext
{
    [Theory]
    [InlineData(AlertKind.Danger)]
    [InlineData(AlertKind.Warning)]
    public void SvAlert_DangerOrWarning_UsesAlertRole(AlertKind kind)
    {
        var cut = Render<SvAlert>(parameters => parameters
            .Add(alert => alert.Id, "account-error")
            .Add(alert => alert.Kind, kind));

        Assert.Equal("alert", cut.Find("#account-error").GetAttribute("role"));
    }

    [Theory]
    [InlineData(AlertKind.Info)]
    [InlineData(AlertKind.Success)]
    public void SvAlert_InfoOrSuccess_UsesStatusRole(AlertKind kind)
    {
        var cut = Render<SvAlert>(parameters => parameters
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
    public void SvAlert_EveryKind_RendersItsKindText(AlertKind kind, string prefix)
    {
        var cut = Render<SvAlert>(parameters => parameters
            .Add(alert => alert.Id, "alert-one")
            .Add(alert => alert.Kind, kind));

        Assert.Contains(prefix, cut.Find("#alert-one").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void SvAlert_DefaultKind_IsInfo()
    {
        var cut = Render<SvAlert>(parameters => parameters.Add(alert => alert.Id, "alert-default"));

        Assert.Equal("status", cut.Find("#alert-default").GetAttribute("role"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SvAlert_TitleWithMarkup_IsEncoded()
    {
        var cut = Render<SvAlert>(parameters => parameters
            .Add(alert => alert.Id, "alert-three")
            .Add(alert => alert.Title, "<b onclick=x>bold</b>"));

        Assert.Empty(cut.FindAll("b"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("a b")]
    public void SvAlert_InvalidId_Throws(string id)
    {
        ThrownExceptions.ArgumentFrom(() => Render<SvAlert>(parameters => parameters.Add(alert => alert.Id, id)));
    }
}
