// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(UserJourneyGroup))]
[Trait("Category", "E2E")]
public sealed class SignInLandingTests(E2EHost host)
{
    [Fact]
    public async Task SignIn_ValidAccount_LandsOnTheVaultAndCreatesANestedFolder()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var topName = "0 Tree " + suffix;
        var childName = "0 Child " + suffix;

        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        var signIn = new LocalSignInPage(page, host.WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(E2EAccounts.UserOne);

        var vault = new VaultPage(page, host.WebUrl);
        await vault.WaitForLandingAsync();
        await vault.CreateFolderAsync(topName);
        var tree = new FolderTreePage(page);
        await tree.CreateChildFolderAsync(topName, childName);
        await vault.FollowBrandLinkFromUnfiledAsync();
        await vault.WaitForLandingAsync();
        session.Errors.AssertClean(page.Url);
    }
}
