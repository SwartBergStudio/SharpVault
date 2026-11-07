// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.UI.Shared.Layout;

namespace SharpVault.UnitTests.UiShared.Layout;

public sealed class SkipLinkTests : BunitContext
{
    public SkipLinkTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void SkipLink_AtRoot_TargetsMainContent()
    {
        var cut = Render<SkipLink>();

        var link = cut.Find("a#skip-to-content");
        Assert.Equal("#main-content", link.GetAttribute("href"));
    }

    [Fact]
    public void SkipLink_LocationChanges_UpdatesHref()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Render<SkipLink>();
        Assert.Equal("#main-content", cut.Find("#skip-to-content").GetAttribute("href"));

        navigation.NavigateTo("account/signed-out");

        cut.WaitForAssertion(() => Assert.Equal("account/signed-out#main-content", cut.Find("#skip-to-content").GetAttribute("href")));

        navigation.NavigateTo("");

        cut.WaitForAssertion(() => Assert.Equal("#main-content", cut.Find("#skip-to-content").GetAttribute("href")));
    }

    [Fact]
    public void SkipLink_Disposed_UnsubscribesFromLocationChanged()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Render<SkipLink>();
        var rendersBefore = cut.RenderCount;
        var markupBefore = cut.Markup;

        cut.Instance.Dispose();
        var thrown = Record.Exception(() => navigation.NavigateTo("account"));

        Assert.Null(thrown);
        Assert.Equal(rendersBefore, cut.RenderCount);
        Assert.Equal(markupBefore, cut.Markup);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("account?token=SV-SECRET-QUERY-77a1", "SV-SECRET-QUERY-77a1", "account#main-content")]
    [InlineData("account#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-FRAGMENT-77a1", "account#main-content")]
    [InlineData("?returnUrl=SV-SECRET-QUERY-77a1", "SV-SECRET-QUERY-77a1", "#main-content")]
    public void SkipLink_UrlWithQueryOrFragment_HrefNeverContainsQueryStringOrFragment(string location, string secret, string expectedHref)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(location);

        var cut = Render<SkipLink>();

        var href = cut.Find("#skip-to-content").GetAttribute("href");
        Assert.Equal(expectedHref, href);
        Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("?", href, StringComparison.Ordinal);
        Assert.Equal(1, href!.Count(character => character == '#'));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SkipLink_PathWithMarkupCharacters_IsEncodedInHref()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("account/%3Cscript%3E");

        var cut = Render<SkipLink>();

        var link = cut.Find("#skip-to-content");
        Assert.Equal(["id", "href"], link.Attributes.Select(attribute => attribute.Name).Where(name => name != "class").ToArray());
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain(link.Attributes, attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SkipLink_DoubleSlashPath_DoesNotBecomeProtocolRelativeUrl()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("http://localhost//evil.example/path");

        var cut = Render<SkipLink>();

        var href = cut.Find("#skip-to-content").GetAttribute("href");
        Assert.NotNull(href);
        Assert.False(href.StartsWith("//", StringComparison.Ordinal), $"Skip link href is protocol-relative: {href}");
        Assert.DoesNotContain("://", href, StringComparison.Ordinal);
    }
}
