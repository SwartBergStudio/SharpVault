// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(LockJourneyGroup))]
[Trait("Category", "E2E")]
public sealed class LockJourneyTests(E2EHost host)
{
    private const string UsernameValue = "user.one@sharpvault.invalid";
    private const string ValueMarkerPrefix = "lock-journey-value-";
    private const string LockReasonPrefix = "Left the company for the revalidation check ";
    private const float RevalidationTimeoutMilliseconds = 150000;

    [Fact]
    public async Task LockedUser_OpenPageWithRevealedValue_LeavesTheDomAtTheNextRevalidation()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var loginName = "0 Lock Login " + suffix;
        var valueMarker = ValueMarkerPrefix + suffix;

        await using var userOne = await host.OpenSessionAsync();
        var userOnePage = await userOne.NewPageAsync();
        var userOneSignIn = new LocalSignInPage(userOnePage, host.WebUrl);
        await userOneSignIn.OpenAsync();
        await userOneSignIn.SignInAsAsync(E2EAccounts.UserOne);
        var userOneVault = new VaultPage(userOnePage, host.WebUrl);
        await userOneVault.OpenAsync();
        await userOneVault.CreateLoginAsync(loginName, UsernameValue, valueMarker);
        await new SecretDetailPage(userOnePage).RevealValueAsync(valueMarker);

        await using var adminOne = await host.OpenSessionAsync();
        var adminOnePage = await adminOne.NewPageAsync();
        var adminOneSignIn = new LocalSignInPage(adminOnePage, host.WebUrl);
        await adminOneSignIn.OpenAsync();
        await adminOneSignIn.SignInAsAsync(E2EAccounts.AdminOne);
        var adminUsers = new AdminUsersPage(adminOnePage, host.WebUrl);
        await adminUsers.OpenAsync();
        await adminUsers.LockAsync(E2EAccounts.UserOneId, LockReasonPrefix + suffix + ".");

        var userOneShell = new ShellPage(userOnePage);
        await userOneShell.AssertSignInRequiredAsync(RevalidationTimeoutMilliseconds);

        Assert.False(await userOneShell.PageHtmlContainsAsync(valueMarker), "The revealed value is still in the page after the lock was revalidated.");
        userOne.Errors.AssertClean(userOnePage.Url);
    }

    [Fact]
    public async Task LockedUser_FreshSignIn_LandsOnAccessDenied()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        await using var adminOne = await host.OpenSessionAsync();
        var adminOnePage = await adminOne.NewPageAsync();
        var adminOneSignIn = new LocalSignInPage(adminOnePage, host.WebUrl);
        await adminOneSignIn.OpenAsync();
        await adminOneSignIn.SignInAsAsync(E2EAccounts.AdminOne);
        var adminUsers = new AdminUsersPage(adminOnePage, host.WebUrl);
        await adminUsers.OpenAsync();
        await adminUsers.LockAsync(E2EAccounts.UserTwoId, LockReasonPrefix + suffix + ".");

        await using var userTwo = await host.OpenSessionAsync();
        var userTwoPage = await userTwo.NewPageAsync();
        var userTwoSignIn = new LocalSignInPage(userTwoPage, host.WebUrl);
        await userTwoSignIn.OpenAsync();
        await userTwoSignIn.SignInAsAsync(E2EAccounts.UserTwo);

        await new ShellPage(userTwoPage).AssertAccessDeniedAsync();
        userTwo.Errors.AssertClean(userTwoPage.Url);
    }
}
