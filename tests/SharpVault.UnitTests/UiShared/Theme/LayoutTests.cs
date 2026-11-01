// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class LayoutTests : BunitContext
{
    private const string DisplayName = "Ada Lovelace";

    public LayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton<AntiforgeryStateProvider, StubAntiforgeryStateProvider>();
    }

    [Fact]
    public void MainLayout_Rendered_HasSkipLinkHeaderAndMainLandmark()
    {
        AddAuthorization();

        var cut = RenderMainLayout();

        Assert.NotNull(cut.Find("a#skip-to-content"));
        Assert.NotNull(cut.Find("header#app-header"));
        var main = cut.Find("main#main-content");
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        Assert.Equal("Page body", cut.Find("main#main-content #app-shell #page-body").TextContent);
        Assert.Single(cut.FindAll("main"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_Rendered_HasUniqueIds()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = RenderMainLayout();

        var ids = cut.FindAll("[id]").Select(element => element.Id).ToList();
        Assert.NotEmpty(ids);
        var duplicates = ids.GroupBy(id => id, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key).ToList();
        Assert.True(duplicates.Count == 0, $"Duplicate ids: {string.Join(", ", duplicates)}");
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_SignedOut_HasUniqueIds()
    {
        AddAuthorization();

        var cut = RenderMainLayout();

        var ids = cut.FindAll("[id]").Select(element => element.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MainLayout_Rendered_ErrorUiDismissIsLabelledButton()
    {
        AddAuthorization();

        var cut = RenderMainLayout();

        var errorUi = cut.Find("#blazor-error-ui");
        Assert.Equal("alert", errorUi.GetAttribute("role"));
        var dismiss = cut.Find("#blazor-error-dismiss");
        Assert.Equal("button", dismiss.LocalName);
        Assert.Equal("button", dismiss.GetAttribute("type"));
        Assert.Contains("dismiss", dismiss.ClassList);
        Assert.Equal("Dismiss", dismiss.GetAttribute("aria-label"));
        var reload = cut.Find("#blazor-error-reload");
        Assert.Contains("reload", reload.ClassList);
        Assert.NotNull(cut.Find("#blazor-error-ui #blazor-error-dismiss"));
        Assert.NotNull(cut.Find("#blazor-error-ui #blazor-error-reload"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void MainLayout_Rendered_ContainsNoInlineStyleScriptOrHandlers()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = RenderMainLayout();

        Assert.Empty(cut.FindAll("[style]"));
        Assert.Empty(cut.FindAll("style"));
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("javascript:", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(cut.FindAll("*"), element => element.Attributes.Any(attribute => attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void SkipLink_AtRoot_TargetsMainContent()
    {
        var cut = Render<SkipLink>();

        var link = cut.Find("a#skip-to-content");
        Assert.Equal("#main-content", link.GetAttribute("href"));
        Assert.Equal("Skip to content", link.TextContent);
        Assert.Contains("sv-skip-link", link.ClassList);
    }

    [Fact]
    public void SkipLink_OnNestedRoute_TargetsMainContentOnSamePage()
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("account");

        var cut = Render<SkipLink>();

        Assert.Equal("account#main-content", cut.Find("#skip-to-content").GetAttribute("href"));
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
    [InlineData("account?token=SV-SECRET-QUERY-77a1#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-QUERY-77a1", "account#main-content")]
    [InlineData("account?token=SV-SECRET-QUERY-77a1#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-FRAGMENT-77a1", "account#main-content")]
    [InlineData("?returnUrl=SV-SECRET-QUERY-77a1", "SV-SECRET-QUERY-77a1", "#main-content")]
    [InlineData("#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-FRAGMENT-77a1", "#main-content")]
    [InlineData("account/signed-out?a=b&c=SV-SECRET-QUERY-77a1", "SV-SECRET-QUERY-77a1", "account/signed-out#main-content")]
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
        Assert.EndsWith("#main-content", href, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("account?token=SV-SECRET-QUERY-77a1", "SV-SECRET-QUERY-77a1")]
    [InlineData("account#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-FRAGMENT-77a1")]
    [InlineData("account?token=SV-SECRET-QUERY-77a1#SV-SECRET-FRAGMENT-77a1", "SV-SECRET-QUERY-77a1")]
    public void SkipLink_NavigationToUrlWithQueryOrFragment_HrefNeverContainsQueryStringOrFragment(string location, string secret)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        var cut = Render<SkipLink>();

        navigation.NavigateTo(location);

        cut.WaitForAssertion(() => Assert.Equal("account#main-content", cut.Find("#skip-to-content").GetAttribute("href")));
        Assert.DoesNotContain(secret, cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("?", cut.Find("#skip-to-content").GetAttribute("href"), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("account/%3Cscript%3E")]
    [InlineData("a%22%20onclick%3D%22alert(1)")]
    [InlineData("a%20b")]
    public void SkipLink_PathWithMarkupCharacters_IsEncodedInHref(string location)
    {
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(location);

        var cut = Render<SkipLink>();

        var link = cut.Find("#skip-to-content");
        Assert.Equal(["id", "class", "href"], link.Attributes.Select(attribute => attribute.Name).ToArray());
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

    [Fact]
    public void SideNav_SignedOut_RendersNothing()
    {
        AddAuthorization();

        var cut = Render<SideNav>();

        Assert.True(string.IsNullOrWhiteSpace(cut.Markup), $"Signed-out side nav rendered: {cut.Markup}");
        Assert.Empty(cut.FindAll("nav"));
    }

    [Fact]
    public void SideNav_SignedIn_EveryLinkHasIdIconAndText()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<SideNav>();

        var nav = cut.Find("nav#side-nav");
        Assert.Equal("Main", nav.GetAttribute("aria-label"));
        var links = cut.FindAll("nav#side-nav a");
        Assert.NotEmpty(links);
        Assert.All(links, link =>
        {
            Assert.False(string.IsNullOrWhiteSpace(link.Id));
            Assert.Contains("sv-nav__link", link.ClassList);
            Assert.NotNull(link.QuerySelector("svg.sv-icon"));
            var label = link.QuerySelector("span");
            Assert.NotNull(label);
            Assert.False(string.IsNullOrWhiteSpace(label.TextContent));
        });
        var home = cut.Find("a#nav-home");
        Assert.Equal("Home", home.QuerySelector("span")?.TextContent);
    }

    [Fact]
    public void SideNav_SignedInOnHome_MarksHomeLinkAsCurrentPage()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<SideNav>();

        var home = cut.Find("a#nav-home");
        Assert.Equal("page", home.GetAttribute("aria-current"));
        Assert.Contains("active", home.ClassList);
    }

    [Fact]
    public void SideNav_SignedInOnOtherPage_DoesNotMarkHomeLinkAsCurrent()
    {
        AddAuthorization().SetAuthorized(DisplayName);
        Services.GetRequiredService<NavigationManager>().NavigateTo("account");

        var cut = Render<SideNav>();

        var home = cut.Find("a#nav-home");
        Assert.False(home.HasAttribute("aria-current"));
        Assert.DoesNotContain("active", home.ClassList);
    }

    [Fact]
    public void AppHeader_Rendered_BrandLinkHasTextAndDecorativeMark()
    {
        AddAuthorization();

        var cut = Render<AppHeader>();

        var brand = cut.Find("header#app-header a#brand-home-link");
        Assert.Contains("SharpVault", brand.TextContent, StringComparison.Ordinal);
        var mark = brand.QuerySelector("svg");
        Assert.NotNull(mark);
        Assert.Equal("true", mark.GetAttribute("aria-hidden"));
        Assert.Contains("vault-door--mark", mark.ClassList);
        Assert.NotNull(cut.Find("header#app-header nav#account-menu"));
    }

    [Fact]
    public void AccountMenu_SignedIn_KeepsIdsAndTexts()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<AccountMenu>();

        Assert.Equal(DisplayName, cut.Find("#account-name").TextContent);
        Assert.Equal(0, cut.Find("#account-name").ChildElementCount);
        var accountLink = cut.Find("#account-link");
        Assert.Equal("Account", accountLink.TextContent);
        Assert.Equal("account", accountLink.GetAttribute("href"));
        var form = cut.Find("form#sign-out-form");
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.Equal(AccountRoutes.SignOut, form.GetAttribute("action"));
        var button = form.QuerySelector("button#sign-out-button");
        Assert.NotNull(button);
        Assert.Contains("Sign out", button.TextContent, StringComparison.Ordinal);
        Assert.Equal("submit", button.GetAttribute("type"));
        Assert.Empty(cut.FindAll("#sign-in-link"));
    }

    [Fact]
    public void AccountMenu_SignedOut_RendersPrimarySignInLink()
    {
        AddAuthorization();

        var cut = Render<AccountMenu>();

        var link = cut.Find("a#sign-in-link");
        Assert.Contains("sv-btn--primary", link.ClassList);
        Assert.Equal(AccountRoutes.SignIn, link.GetAttribute("href"));
        Assert.Contains("Sign in", link.TextContent, StringComparison.Ordinal);
        Assert.NotNull(link.QuerySelector("svg.sv-icon"));
        Assert.Empty(cut.FindAll("#sign-out-form"));
        Assert.Empty(cut.FindAll("#sign-out-button"));
        Assert.Empty(cut.FindAll("#account-name"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_SignedOut_ShowsNoUserData()
    {
        AddAuthorization();

        var cut = Render<AccountMenu>();

        Assert.DoesNotContain(DisplayName, cut.Markup, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("form"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_DisplayNameWithMarkup_IsHtmlEncoded()
    {
        const string hostile = "<img src=x onerror=alert(1)><script>alert(2)</script>";
        AddAuthorization().SetAuthorized(hostile);

        var cut = Render<AccountMenu>();

        Assert.Equal(hostile, cut.Find("#account-name").TextContent);
        Assert.Empty(cut.FindAll("img"));
        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("<img", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountMenu_SignOutForm_PostsToSignOutRouteOnly()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<AccountMenu>();

        var form = cut.Find("#sign-out-form");
        Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
        Assert.Equal("/account/sign-out", form.GetAttribute("action"));
        Assert.DoesNotContain(cut.FindAll("a"), anchor => anchor.GetAttribute("href")?.Contains("sign-out", StringComparison.Ordinal) == true);
    }

    private IRenderedComponent<MainLayout> RenderMainLayout() =>
        Render<MainLayout>(parameters => parameters.Add(layout => layout.Body, "<p id=\"page-body\">Page body</p>"));
}
