// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public sealed class ShellPage(IPage page) : PageObject(page)
{
    private const string SignOutButtonId = "sign-out-button";
    private const string NavVaultId = "nav-vault";
    private const string NavSharedId = "nav-shared";
    private const string NavRecycleBinId = "nav-recycle-bin";
    private const string NavAccountId = "nav-account";
    private const string NavAdminUsersId = "nav-admin-users";
    private const string NavAdminTransfersId = "nav-admin-transfers";
    private const string AppShellId = "app-shell";
    private const string AccessDeniedHeadingId = "access-denied-heading";
    private const string SignedOutHeadingId = "signed-out-heading";
    private const string AdministratorsOnlyPanelId = "admin-users-forbidden";
    private const string NotAuthorizedPanelId = "not-authorized";
    private const string BlazorErrorBannerId = "blazor-error-ui";

    public Task WaitForShellAsync() => ShowsAsync(AppShellId);

    public Task OpenVaultAsync() => ClickAsync(NavVaultId);

    public Task OpenSharedAsync() => ClickAsync(NavSharedId);

    public Task OpenRecycleBinAsync() => ClickAsync(NavRecycleBinId);

    public Task OpenAccountAsync() => ClickAsync(NavAccountId);

    public Task OpenAdminUsersAsync() => ClickAsync(NavAdminUsersId);

    public Task OpenAdminTransfersAsync() => ClickAsync(NavAdminTransfersId);

    public Task AssertAccessDeniedAsync() => ShowsAsync(AccessDeniedHeadingId);

    public Task AssertBlazorErrorBannerHiddenAsync() => HidesAsync(BlazorErrorBannerId);

    public Task AssertSignInRequiredAsync(float timeoutMilliseconds) => ShowsAsync(NotAuthorizedPanelId, timeoutMilliseconds);

    public Task AssertSignInRequiredHiddenAsync() => HidesAsync(NotAuthorizedPanelId);

    public Task<bool> PageHtmlContainsAsync(string text) =>
        Page.EvaluateAsync<bool>("text => document.documentElement.outerHTML.includes(text)", text);

    public Task AssertAdministratorsOnlyAsync() => ShowsAsync(AdministratorsOnlyPanelId);

    public Task AssertSignedOutAsync() => ShowsAsync(SignedOutHeadingId);

    public async Task SignOutAsync()
    {
        await ClickAsync(SignOutButtonId);
    }
}
