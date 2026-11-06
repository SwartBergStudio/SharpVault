// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(AdminJourneyGroup))]
[Trait("Category", "E2E")]
public sealed class AdminJourneyTests(E2EHost host)
{
    private const string UsernameValue = "user.one@sharpvault.invalid";
    private const string ValuePrefix = "admin-journey-value-";
    private const string TransferFolderPrefix = "Transferred from User One";
    private const string Completed = "Completed";

    private static readonly Regex TransferDetailUrl = new(@"/admin/transfers/[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z", RegexOptions.CultureInvariant);
    private const string LockReasonPrefix = "Left the company after the notice period ended in ";
    private const string TransferReasonPrefix = "Transfer of the vault to a colleague after the notice period in ";
    private const string CountOfOneSecret = "1";
    private const string CountOfNoShares = "0";

    [Fact]
    public async Task AdminJourney_LockTransferApprove_TargetReceivesAndLockedUserIsRefused()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var loginName = "0 Admin Login " + suffix;
        var valueMarker = ValuePrefix + suffix;

        await using var userOne = await host.OpenSessionAsync();
        var userOnePage = await userOne.NewPageAsync();
        var userOneSignIn = new LocalSignInPage(userOnePage, host.WebUrl);
        await userOneSignIn.OpenAsync();
        await userOneSignIn.SignInAsAsync(E2EAccounts.UserOne);
        var userOneVault = new VaultPage(userOnePage, host.WebUrl);
        await userOneVault.OpenAsync();
        await userOneVault.CreateLoginAsync(loginName, UsernameValue, valueMarker);

        await using var adminOne = await host.OpenSessionAsync();
        var adminOnePage = await adminOne.NewPageAsync();
        var adminOneSignIn = new LocalSignInPage(adminOnePage, host.WebUrl);
        await adminOneSignIn.OpenAsync();
        await adminOneSignIn.SignInAsAsync(E2EAccounts.AdminOne);
        var adminUsers = new AdminUsersPage(adminOnePage, host.WebUrl);
        await adminUsers.OpenAsync();
        await adminUsers.AssertCountsAsync(E2EAccounts.UserOneId, CountOfOneSecret, CountOfNoShares);
        await adminUsers.LockAsync(E2EAccounts.UserOneId, LockReasonPrefix + suffix + ".");

        await using var adminTwo = await host.OpenSessionAsync();
        var adminTwoPage = await adminTwo.NewPageAsync();
        var adminTwoSignIn = new LocalSignInPage(adminTwoPage, host.WebUrl);
        await adminTwoSignIn.OpenAsync();
        await adminTwoSignIn.SignInAsAsync(E2EAccounts.AdminTwo);

        await new ShellPage(userOnePage).OpenVaultAsync();
        await new ShellPage(userOnePage).AssertAccessDeniedAsync();

        await adminUsers.OpenAsync();
        await adminUsers.StartTransferAsync(E2EAccounts.UserOneId);
        var transfers = new TransfersPage(adminOnePage, host.WebUrl);
        await transfers.RequestAsync(E2EAccounts.UserTwo, TransferReasonPrefix + suffix + ".");
        await adminOnePage.WaitForURLAsync(TransferDetailUrl, new() { Timeout = PageObject.TimeoutMilliseconds });
        var transferUrl = adminOnePage.Url;

        await adminTwoPage.GotoAsync(transferUrl);
        var approver = new TransfersPage(adminTwoPage, host.WebUrl);
        await approver.ApproveAsync();
        await approver.AssertStateAsync(Completed);

        await using var userTwo = await host.OpenSessionAsync();
        var userTwoPage = await userTwo.NewPageAsync();
        var userTwoSignIn = new LocalSignInPage(userTwoPage, host.WebUrl);
        await userTwoSignIn.OpenAsync();
        await userTwoSignIn.SignInAsAsync(E2EAccounts.UserTwo);
        var userTwoVault = new VaultPage(userTwoPage, host.WebUrl);
        await userTwoVault.OpenAsync();
        await userTwoVault.OpenFolderAsync(TransferFolderPrefix);
        await userTwoVault.AssertListShowsAsync(loginName);
        await userTwoVault.OpenItemAsync(loginName);
        await new SecretDetailPage(userTwoPage).RevealValueAsync(valueMarker);

        userOne.Errors.AssertClean(userOnePage.Url);
        adminOne.Errors.AssertClean(adminOnePage.Url);
        adminTwo.Errors.AssertClean(adminTwoPage.Url);
        userTwo.Errors.AssertClean(userTwoPage.Url);
    }

}
