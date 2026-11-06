// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(ServerIdleGroup))]
[Trait("Category", "E2E")]
public sealed class ServerIdleLimitTests(IdleLimitHost host)
{
    private const string IdleMonitorScriptPattern = "**/idle-monitor.js";
    private const string BlockedRequestConsoleText = "net::ERR_FAILED";
    private const string FailedResourceConsoleText = "Failed to load resource";
    private const string CircuitTimeoutConsoleText = "Server timeout elapsed without receiving a message from the server";
    private const float ServerEndsSessionMilliseconds = 330000;
    private const float ReloadCheckMilliseconds = 30000;
    private static readonly TimeSpan ActivityInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan SilentTabRun = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task BlockedIdleScript_NoActivity_ServerEndsTheSession()
    {
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        await page.RouteAsync(IdleMonitorScriptPattern, route => route.AbortAsync());
        session.Errors.IgnoreConsoleErrorsContaining(BlockedRequestConsoleText);
        session.Errors.IgnoreConsoleErrorsContaining(FailedResourceConsoleText);
        session.Errors.IgnoreConsoleErrorsContaining(CircuitTimeoutConsoleText);
        var signIn = new LocalSignInPage(page, host.WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(E2EAccounts.UserOne);
        var vault = new VaultPage(page, host.WebUrl);
        await vault.OpenAsync();

        var shell = new ShellPage(page);
        await shell.AssertSignInRequiredAsync(ServerEndsSessionMilliseconds);

        await page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = PageObject.TimeoutMilliseconds });
        await vault.AssertNewItemHiddenAsync();
        session.Errors.AssertClean(page.Url);
    }

    [Fact]
    public async Task ActiveUserInOneTab_SilentSecondTab_StaysSignedIn()
    {
        await using var session = await host.OpenSessionAsync();
        var active = await session.NewPageAsync();
        var silent = await session.NewPageAsync();
        await silent.RouteAsync(IdleMonitorScriptPattern, route => route.AbortAsync());
        session.Errors.IgnoreConsoleErrorsContaining(BlockedRequestConsoleText);
        session.Errors.IgnoreConsoleErrorsContaining(FailedResourceConsoleText);
        var signIn = new LocalSignInPage(active, host.WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(E2EAccounts.UserOne);
        await silent.GotoAsync(host.WebUrl + Routes.Vault);
        var activeVault = new VaultPage(active, host.WebUrl);
        await activeVault.OpenAsync();

        var deadline = DateTimeOffset.UtcNow + SilentTabRun;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await active.Keyboard.PressAsync("Shift");
            await Task.Delay(ActivityInterval, TestContext.Current.CancellationToken);
        }

        var shell = new ShellPage(active);
        await shell.AssertSignInRequiredHiddenAsync();
        await activeVault.OpenAsync();
        var folderName = "0 Idle Folder " + Guid.NewGuid().ToString("N")[..8];
        await activeVault.CreateFolderAsync(folderName);
        await activeVault.OpenFolderAsync(folderName);
        session.Errors.AssertClean(active.Url);
    }
}
