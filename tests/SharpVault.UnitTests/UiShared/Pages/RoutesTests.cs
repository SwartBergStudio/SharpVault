// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.UI.Shared;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Pages;

public sealed class RoutesTests : ShellTestContext
{
    public RoutesTests()
    {
        SetRendererInfo(new RendererInfo("Server", true));
    }

    [Fact]
    public void Routes_UnknownUrl_RendersTheNotFoundPage()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("no-such-page");

        var cut = Render<Routes>();

        Assert.NotNull(cut.Find("h1#not-found-heading"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Routes_AnonymousOnProtectedPage_ShowsSignInRequiredPanelWithSignInLink()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("account");

        var cut = Render<Routes>();

        var panel = cut.Find("#not-authorized");
        Assert.Equal("alert", panel.GetAttribute("role"));
        Assert.Equal(AccountRoutes.SignIn, cut.Find("a#not-authorized-sign-in").GetAttribute("href"));
        Assert.Empty(cut.FindAll("#account-section"));
    }

    [Fact]
    public void Routes_AdminUsersUrl_RoutesToTheAdminUsersPageWhenSignedIn()
    {
        SignInAs("Admin One", isAdmin: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo("admin/users");

        var cut = Render<Routes>();

        Assert.NotNull(cut.Find("#admin-users-heading"));
    }

    [Fact]
    public void Routes_RoutedAssemblyFromTheHost_ResolvesItsPages()
    {
        RoutedAssemblyList.Add(typeof(RoutesTests).Assembly);
        Services.GetRequiredService<NavigationManager>().NavigateTo("host-routed-page");

        var cut = Render<Routes>();

        Assert.NotNull(cut.Find("#host-routed-page"));
        Assert.Empty(cut.FindAll("#not-found"));
    }

    [Route("/host-routed-page")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public sealed class HostRoutedPage : ComponentBase
    {
        protected override void BuildRenderTree(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder) =>
            builder.AddMarkupContent(0, "<p id=\"host-routed-page\">Host page</p>");
    }
}
