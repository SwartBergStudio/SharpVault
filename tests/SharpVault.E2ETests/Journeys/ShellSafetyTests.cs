// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Microsoft.Playwright;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(ShellSafetyGroup))]
[Trait("Category", "E2E")]
public sealed class ShellSafetyTests(E2EHost host)
{
    private const string IdleMonitorScriptPattern = "**/idle-monitor.js";
    private const string BlockedRequestConsoleText = "net::ERR_FAILED";
    private const string FailedResourceConsoleText = "Failed to load resource";
    private const string BlazorScriptPattern = "**/blazor.web*.js";
    private const string IdleFastForward = "16:00";
    private const string CircuitTimeoutConsoleText = "Server timeout elapsed without receiving a message from the server";
    private const int IdleLimitMinutes = 16;

    private static readonly string[] AdminRoutes =
    [
        Routes.Home,
        Routes.Vault,
        Routes.Unfiled,
        Routes.RecycleBin,
        Routes.Shared,
        Routes.Account,
        Routes.AdminUsers,
        Routes.AdminTransfers,
        Routes.NewTransfer,
        Routes.TransferShares,
    ];

    [Fact]
    public async Task EveryVisitedPage_ProducesZeroCspViolationsAndNoPageErrors()
    {
        await using var admin = await host.OpenSessionAsync();
        var adminPage = await admin.NewPageAsync();
        await SignInAsync(adminPage, E2EAccounts.AdminOne);
        var shell = new ShellPage(adminPage);
        foreach (var route in AdminRoutes)
        {
            await adminPage.GotoAsync(host.WebUrl + route);
            await shell.WaitForShellAsync();
        }

        await using var user = await host.OpenSessionAsync();
        var userPage = await user.NewPageAsync();
        await SignInAsync(userPage, E2EAccounts.UserOne);
        await userPage.GotoAsync(host.WebUrl + Routes.AdminUsers);
        await new ShellPage(userPage).AssertAdministratorsOnlyAsync();

        await new ShellPage(adminPage).SignOutAsync();
        await new ShellPage(adminPage).AssertSignedOutAsync();

        admin.Errors.AssertClean(adminPage.Url);
        user.Errors.AssertClean(userPage.Url);
    }

    [Fact]
    public async Task IdleTimeout_WhenFastForwarded_SignsTheUserOut()
    {
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        await page.Clock.InstallAsync(new ClockInstallOptions { Time = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) });
        await SignInAsync(page, E2EAccounts.UserOne);

        session.Errors.IgnoreConsoleErrorsContaining(CircuitTimeoutConsoleText);
        await page.Clock.FastForwardAsync(IdleFastForward);

        await new ShellPage(page).AssertSignedOutAsync();
        session.Errors.AssertClean(page.Url);
    }

    [Fact]
    public async Task IdleTimeout_TabClosedAndReopenedAfterTimeout_SignsTheUserOut()
    {
        await using var session = await host.OpenSessionAsync();
        var signedInAt = DateTimeOffset.UtcNow;
        var firstPage = await session.NewPageAsync();
        await firstPage.Clock.InstallAsync(new ClockInstallOptions { Time = signedInAt.ToString("O", CultureInfo.InvariantCulture) });
        await SignInAsync(firstPage, E2EAccounts.UserOne);
        await firstPage.CloseAsync();

        var reopened = await session.NewPageAsync();
        await reopened.Clock.InstallAsync(new ClockInstallOptions { Time = signedInAt.AddMinutes(IdleLimitMinutes).ToString("O", CultureInfo.InvariantCulture) });
        await reopened.GotoAsync(host.WebUrl + Routes.Vault);

        await new ShellPage(reopened).AssertSignedOutAsync();
        session.Errors.AssertClean(reopened.Url);
    }

    [Fact]
    public async Task IdleMonitorBlocked_AppKeepsWorkingWithNoErrorBanner()
    {
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        await page.RouteAsync(IdleMonitorScriptPattern, route => route.AbortAsync());
        session.Errors.IgnoreConsoleErrorsContaining(BlockedRequestConsoleText);
        session.Errors.IgnoreConsoleErrorsContaining(FailedResourceConsoleText);
        await SignInAsync(page, E2EAccounts.UserOne);

        await page.GotoAsync(host.WebUrl + Routes.Vault);
        var shell = new ShellPage(page);
        await shell.WaitForShellAsync();
        await shell.AssertBlazorErrorBannerHiddenAsync();
        session.Errors.AssertClean(page.Url);
    }

    [Fact]
    public async Task SignedInPage_BeforeTheAppStarts_ShowsTheBootSkeletonAndRemovesItOnceStarted()
    {
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        await SignInAsync(page, E2EAccounts.UserOne);
        var shell = new ShellPage(page);

        await page.RouteAsync(BlazorScriptPattern, route => route.AbortAsync());
        await page.GotoAsync(host.WebUrl + Routes.Vault);
        await shell.AssertBootSkeletonShownAsync();

        await page.UnrouteAsync(BlazorScriptPattern);
        await page.GotoAsync(host.WebUrl + Routes.Vault);
        await shell.WaitForShellAsync();
        await shell.AssertBootSkeletonHiddenAsync();
    }

    [Fact]
    public async Task Dialog_EscapeFromInitialFocus_Closes()
    {
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        await SignInAsync(page, E2EAccounts.UserOne);
        var vault = new VaultPage(page, host.WebUrl);
        await vault.OpenAsync();
        await vault.OpenNewFolderEditorAsync();
        await vault.WaitForFolderEditorFocusAsync();
        await vault.PressEscapeAsync();
        await vault.AssertFolderEditorHiddenAsync();
        session.Errors.AssertClean(page.Url);
    }

    private async Task SignInAsync(IPage page, string displayName)
    {
        var signIn = new LocalSignInPage(page, host.WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(displayName);
    }
}
