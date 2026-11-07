// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using SharpVault.UI.Shared.Components.Common;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.OneTimeLinks;

public sealed class OneTimeLinkBrowserOnlyInputTests : MudTestContext
{
    private const string ValidId = "otl-passphrase";
    private const string Label = "Passphrase";

    [Fact]
    public void InvalidId_Throws()
    {
        Assert.ThrowsAny<Exception>(() => Render<SvBrowserOnlyInput>(parameters => parameters
            .Add(input => input.Id, "Not A Valid Id")
            .Add(input => input.Label, Label)));
    }

    [Fact]
    public void Render_HasNoBlazorEventOrValue()
    {
        var cut = Render<SvBrowserOnlyInput>(parameters => parameters
            .Add(input => input.Id, ValidId)
            .Add(input => input.Label, Label)
            .Add(input => input.Password, true));

        var input = cut.Find("#" + ValidId);
        Assert.Null(input.GetAttribute("value"));
        Assert.DoesNotContain("blazor:", cut.Markup, StringComparison.Ordinal);
        Assert.Equal("password", input.GetAttribute("type"));
    }
}
