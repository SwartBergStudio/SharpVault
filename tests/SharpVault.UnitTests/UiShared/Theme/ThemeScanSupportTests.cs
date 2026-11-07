// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class ThemeScanSupportTests
{
    [Fact]
    public void UiSourceFiles_Repository_FindsRazorAndCssSources()
    {
        Assert.NotEmpty(UiSourceFiles.WithExtension(".razor"));
        Assert.Contains(UiSourceFiles.WithExtension(".css"), file => file.RelativePath.EndsWith("wwwroot/css/app.css", StringComparison.Ordinal));
        Assert.Contains(UiSourceFiles.WithExtension(".razor"), file => file.RelativePath.StartsWith("src/SharpVault.Web/", StringComparison.Ordinal));
        Assert.DoesNotContain(UiSourceFiles.WithExtension(".razor"), file => file.RelativePath.Contains("/obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(UiSourceFiles.WithExtension(".css"), file => file.RelativePath.Contains("/bin/", StringComparison.Ordinal));
    }

    [Fact]
    public void RazorTags_RepositorySources_FindInteractiveElements()
    {
        var found = UiSourceFiles.WithExtension(".razor").SelectMany(file => RazorTags.Find(file.Text)).ToList();

        Assert.NotEmpty(found);
    }

    [Theory]
    [InlineData("<abbr title=\"x\">x</abbr>", 0)]
    [InlineData("<a href=\"a>b\" id=\"x\">", 1)]
    [InlineData("<button type=\"button\" @onclick=\"() => x > 1\" id=\"b\">", 1)]
    [InlineData("<SvTextField Id=\"x\" Label=\"y\" />", 1)]
    [InlineData("<input /><textarea></textarea>", 2)]
    public void RazorTags_Sample_MatchesOnlyInteractiveElements(string markup, int expected)
    {
        Assert.Equal(expected, RazorTags.Find(markup).Count());
    }

    [Fact]
    public void RazorTags_DataIdAttribute_IsNotMistakenForId()
    {
        var tag = Assert.Single(RazorTags.Find("<button data-id=\"1\" type=\"button\">"));

        Assert.False(RazorTags.HasAttribute(tag, "id"));
    }
}
