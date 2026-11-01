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
    [InlineData("<AuthorizeView>", 0)]
    [InlineData("<a href=\"a>b\" id=\"x\">", 1)]
    [InlineData("<button type=\"button\" @onclick=\"() => x > 1\" id=\"b\">", 1)]
    [InlineData("<NavLink id=\"n\" href=\"\">", 1)]
    [InlineData("<input /><textarea></textarea>", 2)]
    public void RazorTags_Sample_MatchesOnlyInteractiveElements(string markup, int expected)
    {
        Assert.Equal(expected, RazorTags.Find(markup).Count());
    }

    [Fact]
    public void RazorTags_QuotedGreaterThan_KeepsFullTag()
    {
        var tag = Assert.Single(RazorTags.Find("<a href=\"a>b\" id=\"x\">"));

        Assert.True(RazorTags.HasAttribute(tag, "id"));
        Assert.Equal("a>b", RazorTags.AttributeValue(tag, "href"));
    }

    [Fact]
    public void RazorTags_DataIdAttribute_IsNotMistakenForId()
    {
        var tag = Assert.Single(RazorTags.Find("<button data-id=\"1\" type=\"button\">"));

        Assert.False(RazorTags.HasAttribute(tag, "id"));
    }

    [Fact]
    public void CssBlocks_NestedMediaAndLayers_AreFlattenedWithContext()
    {
        const string css = "@layer a, b;\n@layer a { .x, .y:not(.z, .w) { color: red; } @media (m) { .q { top: 0; } } }\n:root { --t: 1; }";

        var blocks = CssBlocks.Flatten(css);

        Assert.Equal([ "@layer a", ".x, .y:not(.z, .w)", "@media (m)", ".q", ":root" ], blocks.Select(block => block.Prelude));
        var query = blocks.Single(block => block.Prelude == ".q");
        Assert.Equal(["@layer a", "@media (m)"], query.Context);
        Assert.Equal([".x", ".y:not(.z, .w)"], blocks[1].Selectors);
        Assert.Equal("red", blocks[1].ValueOf("color"));
    }

    [Fact]
    public void CssBlocks_DeclarationsBeforeNestedRule_AreNotMerged()
    {
        var declarations = CssBlocks.Declarations("a: b; .nested { c: d; } e: f");

        Assert.Equal([("a", "b"), ("e", "f")], declarations);
    }
}
