// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpVault.UI.Shared.Models;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Pages;
using SharpVault.UI.Shared.Services;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed class PageTests : BunitContext
{
    private const string DisplayName = "Ada Lovelace";

    private readonly ISharpVaultApiClient _apiClient = Substitute.For<ISharpVaultApiClient>();

    public PageTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSingleton(_apiClient);
    }

    [Fact]
    public void HomePage_SignedOut_RendersStaticHeroDoorAndPrimarySignIn()
    {
        AddAuthorization();

        var cut = Render<Home>();

        var hero = cut.Find(".sv-hero");
        Assert.Equal("home", hero.Id);
        var door = hero.QuerySelector("svg.vault-door");
        Assert.NotNull(door);
        Assert.Contains("vault-door--hero", door.ClassList);
        Assert.DoesNotContain("vault-door--sealing", door.ClassList);
        var signIn = cut.Find("#home-sign-in");
        Assert.Contains("sv-btn--primary", signIn.ClassList);
        Assert.Equal(AccountRoutes.SignIn, signIn.GetAttribute("href"));
        Assert.Equal("SharpVault", cut.Find("#home-heading").TextContent);
        Assert.Empty(cut.FindAll("#home-greeting"));
    }

    [Fact]
    public void HomePage_SignedIn_KeepsGreeting()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<Home>();

        var greeting = cut.Find("#home-greeting");
        Assert.StartsWith("Signed in as", greeting.TextContent, StringComparison.Ordinal);
        Assert.Equal($"Signed in as {DisplayName}", greeting.TextContent);
        Assert.Empty(cut.FindAll("#home-sign-in"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void HomePage_SignedInWithMarkupInName_IsHtmlEncoded()
    {
        AddAuthorization().SetAuthorized("<script>alert(1)</script>");

        var cut = Render<Home>();

        Assert.Empty(cut.FindAll("script"));
        Assert.DoesNotContain("<script", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void HomePage_SignedOut_ShowsNoUserData()
    {
        AddAuthorization();

        var cut = Render<Home>();

        Assert.DoesNotContain("Signed in as", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedOutPage_Rendered_ShowsSealingVaultDoor()
    {
        var cut = Render<SignedOut>();

        var door = cut.Find(".sv-state__visual svg.vault-door");
        Assert.Contains("vault-door--sealing", door.ClassList);
        Assert.Equal("true", door.GetAttribute("aria-hidden"));
        Assert.Equal("You have been signed out", cut.Find("#signed-out-heading").TextContent);
        var link = cut.Find("#signed-out-sign-in-link");
        Assert.Contains("sv-btn--primary", link.ClassList);
        Assert.Equal(AccountRoutes.SignIn, link.GetAttribute("href"));
        Assert.Equal("Sign in again", link.TextContent);
        Assert.Single(cut.FindAll("svg.vault-door--sealing"));
    }

    [Fact]
    public void NotFoundPage_Rendered_UsesStatePanelWithHomeLink()
    {
        var cut = Render<NotFound>();

        var panel = cut.Find("section.sv-state");
        Assert.DoesNotContain("sv-state--danger", panel.ClassList);
        Assert.NotNull(panel.QuerySelector(".sv-state__visual svg.sv-icon--xl"));
        Assert.Equal("404", cut.Find(".sv-plate").TextContent);
        Assert.Equal("Not found", cut.Find("#not-found-heading").TextContent);
        Assert.Equal("The page you are looking for does not exist.", cut.Find("#not-found-message").TextContent);
        var home = cut.Find("#not-found-home-link");
        Assert.Contains("sv-btn", home.ClassList);
        Assert.Equal("", home.GetAttribute("href"));
        Assert.Equal("Home", home.TextContent);
    }

    [Fact]
    public void AccessDeniedPage_Rendered_UsesDangerStateWithIconAndText()
    {
        var cut = Render<AccessDenied>();

        var panel = cut.Find("section.sv-state");
        Assert.Contains("sv-state--danger", panel.ClassList);
        var icon = panel.QuerySelector(".sv-state__visual svg.sv-icon--xl");
        Assert.NotNull(icon);
        Assert.Equal("true", icon.GetAttribute("aria-hidden"));
        Assert.Equal("You do not have access to SharpVault", cut.Find("#access-denied-heading").TextContent);
        Assert.Equal("Ask your administrator to give you access.", cut.Find("#access-denied-message").TextContent);
        var home = cut.Find("#access-denied-home-link");
        Assert.Contains("sv-btn", home.ClassList);
        Assert.Equal("Home", home.TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccessDeniedPage_Rendered_ShowsNoUserData()
    {
        AddAuthorization().SetAuthorized(DisplayName);

        var cut = Render<AccessDenied>();

        Assert.DoesNotContain(DisplayName, cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void SignInErrorPage_Rendered_UsesDangerStateWithRetryLink()
    {
        var cut = Render<SignInError>();

        var panel = cut.Find("section.sv-state");
        Assert.Contains("sv-state--danger", panel.ClassList);
        Assert.NotNull(panel.QuerySelector(".sv-state__visual svg.sv-icon--xl"));
        Assert.Equal("Sign-in failed", cut.Find("#sign-in-error-heading").TextContent);
        Assert.False(string.IsNullOrWhiteSpace(cut.Find("#sign-in-error-message").TextContent));
        var retry = cut.Find("#sign-in-error-retry-link");
        Assert.Contains("sv-btn--primary", retry.ClassList);
        Assert.Equal(AccountRoutes.SignIn, retry.GetAttribute("href"));
        Assert.Equal("Try again", retry.TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SignInErrorPage_Rendered_ShowsNoExceptionDetails()
    {
        var cut = Render<SignInError>();

        Assert.DoesNotContain("Exception", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AADSTS", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void AccountPage_AdminProfile_RendersMonospaceIdAndAdminBadgeWithText()
    {
        var userId = Guid.NewGuid();
        ReturnProfile(new CurrentUserProfile(userId, DisplayName, true));

        var cut = Render<Account>();

        var id = cut.Find("#account-user-id");
        Assert.Contains("sv-mono", id.ClassList);
        Assert.Equal(userId.ToString(), id.TextContent);
        Assert.Equal(0, id.ChildElementCount);
        var name = cut.Find("#account-display-name");
        Assert.Equal(DisplayName, name.TextContent);
        Assert.Equal(0, name.ChildElementCount);
        var badge = cut.Find("#account-admin-badge");
        Assert.Equal("Administrator", badge.TextContent);
        Assert.Contains("sv-tag--accent", badge.ClassList);
        Assert.NotNull(badge.ParentElement?.QuerySelector("svg.sv-icon"));
        Assert.Equal("Your account", cut.Find("#account-heading").TextContent);
        Assert.Empty(cut.FindAll("#account-error"));
    }

    [Fact]
    public void AccountPage_NonAdminProfile_RendersNoAdminBadge()
    {
        ReturnProfile(new CurrentUserProfile(Guid.NewGuid(), DisplayName, false));

        var cut = Render<Account>();

        Assert.NotNull(cut.Find("#account-section"));
        Assert.Empty(cut.FindAll("#account-admin-badge"));
        Assert.DoesNotContain("Administrator", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_ProfileWithMarkupInName_IsHtmlEncoded()
    {
        ReturnProfile(new CurrentUserProfile(Guid.NewGuid(), "<img src=x onerror=alert(1)>", false));

        var cut = Render<Account>();

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("<img src=x onerror=alert(1)>", cut.Find("#account-display-name").TextContent);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_ApiFailure_RendersDangerAlertWithExistingId()
    {
        _apiClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserProfile>.Failed());

        var cut = Render<Account>();

        var alert = cut.Find("#account-error");
        Assert.Equal("alert", alert.GetAttribute("role"));
        Assert.Contains("sv-alert--danger", alert.ClassList);
        Assert.Contains("Your account details could not be loaded", alert.TextContent, StringComparison.Ordinal);
        Assert.Empty(cut.FindAll("#account-section"));
        Assert.Empty(cut.FindAll("#account-user-id"));
    }

    [Fact]
    public void AccountPage_SuccessWithoutValue_RendersDangerAlert()
    {
        _apiClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(new ApiResponse<CurrentUserProfile>(ApiCallStatus.Success, null));

        var cut = Render<Account>();

        Assert.Equal("alert", cut.Find("#account-error").GetAttribute("role"));
        Assert.Empty(cut.FindAll("#account-section"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_SignInRequired_NavigatesToSignInWithEscapedReturnUrlAndShowsNothing()
    {
        _apiClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserProfile>.SignInRequired());

        var cut = Render<Account>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal($"http://localhost{AccountRoutes.SignIn}?returnUrl=%2Faccount", navigation.Uri);
        Assert.Empty(cut.FindAll("#account-section"));
        Assert.Empty(cut.FindAll("#account-error"));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AccountPage_AccessDenied_NavigatesToAccessDeniedPageAndShowsNothing()
    {
        _apiClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserProfile>.AccessDenied());

        var cut = Render<Account>();

        var navigation = Services.GetRequiredService<NavigationManager>();
        Assert.Equal($"http://localhost{AccountRoutes.AccessDenied}", navigation.Uri);
        Assert.Empty(cut.FindAll("#account-section"));
        Assert.Empty(cut.FindAll("#account-error"));
    }

    [Fact]
    public void AccountPage_Rendered_CallsApiExactlyOnce()
    {
        ReturnProfile(new CurrentUserProfile(Guid.NewGuid(), DisplayName, false));

        Render<Account>();

        _apiClient.Received(1).GetCurrentUserAsync(Arg.Any<CancellationToken>());
    }

    private void ReturnProfile(CurrentUserProfile profile) =>
        _apiClient.GetCurrentUserAsync(Arg.Any<CancellationToken>()).Returns(ApiResponse<CurrentUserProfile>.Success(profile));
}
