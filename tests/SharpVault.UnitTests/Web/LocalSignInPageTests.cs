// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Authentication.Local;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UnitTests.Support.Theme;
using WebApp::SharpVault.Web.Components.Pages;

namespace SharpVault.UnitTests.Web;

public sealed class LocalSignInPageTests : ShellTestContext
{
    private const string AntiforgeryToken = "SV-TEST-ANTIFORGERY-TOKEN";

    public LocalSignInPageTests()
    {
        Services.AddSingleton<AntiforgeryStateProvider>(new FixedAntiforgeryStateProvider());
    }

    [Fact]
    public void LocalSignInPage_ShowsFourFormsWithAntiforgeryAndNoGuidInHrefs()
    {
        var cut = Render<LocalSignIn>();

        var forms = cut.FindAll("#local-account-list form");
        Assert.Equal(4, forms.Count);
        Assert.All(forms, form =>
        {
            Assert.Equal("post", form.GetAttribute("method"), ignoreCase: true);
            Assert.Equal(AccountRoutes.LocalSignIn, form.GetAttribute("action"));
            var token = form.QuerySelector("input[name=__RequestVerificationToken]");
            Assert.NotNull(token);
            Assert.Equal(AntiforgeryToken, token.GetAttribute("value"));
            Assert.NotNull(form.QuerySelector("input[type=hidden][name=accountId]"));
            Assert.NotNull(form.QuerySelector("button[type=submit]"));
        });
        foreach (var account in LocalAccounts.All)
        {
            Assert.DoesNotContain(
                cut.FindAll("[href]"),
                element => element.GetAttribute("href")!.Contains(account.Id.ToString("D"), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void LocalSignInPage_EveryAccount_HasSubmitButtonRolesAndKebabCaseIds()
    {
        var cut = Render<LocalSignIn>();

        foreach (var account in LocalAccounts.All)
        {
            var slug = account.DisplayName.ToLowerInvariant().Replace(' ', '-');
            Assert.Contains(account.DisplayName, cut.Find($"#local-account-{slug}").TextContent, StringComparison.Ordinal);
            Assert.Equal(string.Join(", ", account.Roles), cut.Find($"#local-account-roles-{slug}").TextContent);
            Assert.Equal(account.Id.ToString("D"), cut.Find($"#local-account-id-{slug}").GetAttribute("value"));
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void LocalSignInPage_HostileReturnUrl_IsSanitisedInEveryForm()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("account/local-sign-in?returnUrl=https%3A%2F%2Fevil.example%2F");

        var cut = Render<LocalSignIn>();

        var values = cut.FindAll("input[name=returnUrl]").Select(input => input.GetAttribute("value")).ToList();
        Assert.Equal(4, values.Count);
        Assert.All(values, value => Assert.Equal("/", value));
    }

    [Fact]
    public void LocalSignInPage_ValidReturnUrl_IsKeptInEveryForm()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("account/local-sign-in?returnUrl=%2Faccount");

        var cut = Render<LocalSignIn>();

        Assert.All(cut.FindAll("input[name=returnUrl]"), input => Assert.Equal("/account", input.GetAttribute("value")));
    }

    private sealed class FixedAntiforgeryStateProvider : AntiforgeryStateProvider
    {
        public override AntiforgeryRequestToken? GetAntiforgeryToken() =>
            new(AntiforgeryToken, "__RequestVerificationToken");
    }
}
