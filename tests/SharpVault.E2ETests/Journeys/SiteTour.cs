// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(SiteTourGroup))]
[Trait("Category", "SiteTour")]
public sealed class SiteTour(E2EHost host)
{
    private const string ScreenshotDirectoryVariable = "SHARPVAULT_E2E_SCREENSHOT_DIR";
    private const string DefaultScreenshotFolderName = "sharpvault-site-tour";
    private const int DesktopWidth = 1366;
    private const int DesktopHeight = 900;
    private const int PhoneWidth = 390;
    private const int PhoneHeight = 844;
    private const string TourFolderName = "0 Tour Folder";
    private const string TourLoginName = "0 Tour Login";
    private const string TourLoginUsername = "tour@sharpvault.invalid";
    private const string TourLoginValue = "site-tour-placeholder";
    private const string SecretDetailScreenName = "secret-detail";

    private static readonly (string Name, string Route)[] UserScreens =
    [
        ("vault", Routes.Vault),
        ("unfiled", Routes.Unfiled),
        ("recycle-bin", Routes.RecycleBin),
        ("shared", Routes.Shared),
        ("account", Routes.Account),
        ("transfer-shares", Routes.TransferShares),
    ];

    private static readonly (string Name, string Route)[] AdminScreens =
    [
        .. UserScreens,
        ("admin-users", Routes.AdminUsers),
        ("admin-transfers", Routes.AdminTransfers),
        ("admin-new-transfer", Routes.NewTransfer),
    ];

    [Fact]
    public async Task SiteTour_AdminAndUser_VisitEveryMainScreenAtBothWidthsAndSaveScreenshots()
    {
        var folder = ResolveScreenshotFolder();

        await TourAsync(E2EAccounts.AdminOne, AdminScreens, folder);
        await TourAsync(E2EAccounts.UserOne, UserScreens, folder);
    }

    private static string ResolveScreenshotFolder()
    {
        var configured = Environment.GetEnvironmentVariable(ScreenshotDirectoryVariable);
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Path.GetTempPath(), DefaultScreenshotFolderName)
            : configured;
        Directory.CreateDirectory(folder);
        TestContext.Current.TestOutputHelper?.WriteLine($"Site tour screenshots: {folder}");
        return folder;
    }

    private async Task TourAsync(string account, IReadOnlyList<(string Name, string Route)> screens, string folder)
    {
        var slug = account.ToLowerInvariant().Replace(' ', '-');
        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        var tour = new SiteTourPage(page, host.WebUrl);
        await tour.UseViewportAsync(DesktopWidth, DesktopHeight);

        var signIn = new LocalSignInPage(page, host.WebUrl);
        await signIn.OpenAsync();
        await tour.SaveScreenshotAsync(Path.Combine(folder, $"{slug}-{DesktopWidth}-sign-in.png"));
        await signIn.SignInAsAsync(account);

        var secretId = await SeedAsync(page);
        var allScreens = screens
            .Append((SecretDetailScreenName, Routes.VaultSecretPrefix + secretId.ToString("D")))
            .ToList();

        foreach (var (width, height) in new[] { (DesktopWidth, DesktopHeight), (PhoneWidth, PhoneHeight) })
        {
            await tour.UseViewportAsync(width, height);
            foreach (var (name, route) in allScreens)
            {
                await tour.VisitAsync(route);
                await tour.SaveScreenshotAsync(Path.Combine(folder, $"{slug}-{width}-{name}.png"));
            }
        }

        session.Errors.AssertClean(page.Url);
    }

    private async Task<Guid> SeedAsync(IPage page)
    {
        var vault = new VaultPage(page, host.WebUrl);
        await vault.OpenAsync();
        await vault.CreateFolderAsync(TourFolderName);
        await vault.OpenAsync();
        await vault.CreateLoginAsync(TourLoginName, TourLoginUsername, TourLoginValue);
        return new SecretDetailPage(page).SecretId();
    }
}
