// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(UserJourneyGroup))]
[Trait("Category", "E2E")]
public sealed class UserJourneyTests(E2EHost host)
{
    private const string UsernameValue = "user.one@sharpvault.invalid";
    private const string NoteMarkerPrefix = "notes-marker-";
    private const string ValueMarkerPrefix = "value-marker-";

    [Fact]
    public async Task UserJourney_SignInFolderLoginNoteShare_RecipientSeesItAndOwnerRevokes()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var folderName = "0 Folder " + suffix;
        var loginName = "0 Login " + suffix;
        var noteName = "0 Note " + suffix;
        var valueMarker = ValueMarkerPrefix + suffix;
        var noteMarker = NoteMarkerPrefix + suffix;

        await using var owner = await host.OpenSessionAsync();
        var ownerPage = await owner.NewPageAsync();
        var ownerSignIn = new LocalSignInPage(ownerPage, host.WebUrl);
        await ownerSignIn.OpenAsync();
        await ownerSignIn.SignInAsAsync(E2EAccounts.UserOne);

        var ownerVault = new VaultPage(ownerPage, host.WebUrl);
        await ownerVault.OpenAsync();
        await ownerVault.CreateFolderAsync(folderName);
        await ownerVault.OpenFolderAsync(folderName);
        await ownerVault.CreateLoginAsync(loginName, UsernameValue, valueMarker);
        var loginDetail = new SecretDetailPage(ownerPage);
        await loginDetail.RevealValueAsync(valueMarker);
        var loginId = loginDetail.SecretId();

        await ownerVault.OpenAsync();
        await ownerVault.CreateSecureNoteAsync(noteName, noteMarker);
        await new SecretDetailPage(ownerPage).RevealNotesAsync(noteMarker);
        await ownerVault.OpenAsync();
        await ownerVault.AssertListShowsAsync(noteName);

        await OpenSecretAsync(ownerPage, loginId);
        await new SharePanelPage(ownerPage).ShareWithAsync(E2EAccounts.UserTwo, E2EAccounts.UserTwoId, E2EAccounts.UserTwo);

        await using var recipient = await host.OpenSessionAsync();
        var recipientPage = await recipient.NewPageAsync();
        var recipientSignIn = new LocalSignInPage(recipientPage, host.WebUrl);
        await recipientSignIn.OpenAsync();
        await recipientSignIn.SignInAsAsync(E2EAccounts.UserTwo);

        var shared = new SharedWithMePage(recipientPage, host.WebUrl);
        await shared.OpenAsync();
        await shared.AssertItemUnderOwnerAsync(E2EAccounts.UserOneId, loginName);
        await shared.OpenItemAsync(E2EAccounts.UserOneId, loginName);
        await new SecretDetailPage(recipientPage).RevealValueAsync(valueMarker);

        await OpenSecretAsync(ownerPage, loginId);
        await new SharePanelPage(ownerPage).RevokeAsync(E2EAccounts.UserTwoId);

        await recipientPage.GotoAsync(host.WebUrl + Routes.SharedSecretPrefix + E2EAccounts.UserOneId.ToString("D") + Routes.SecretsSegment + loginId.ToString("D"));
        await new SharedSecretPage(recipientPage).AssertAccessEndedAsync();

        owner.Errors.AssertClean(ownerPage.Url);
        recipient.Errors.AssertClean(recipientPage.Url);
    }

    private async Task OpenSecretAsync(IPage page, Guid secretId)
    {
        await page.GotoAsync(host.WebUrl + Routes.VaultSecretPrefix + secretId.ToString("D"));
        await new SecretDetailPage(page).WaitForAsync();
    }
}
