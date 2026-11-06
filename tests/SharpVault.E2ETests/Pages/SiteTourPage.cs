// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public sealed class SiteTourPage(IPage page, string webUrl) : PageObject(page)
{
    public Task UseViewportAsync(int width, int height) => Page.SetViewportSizeAsync(width, height);

    public async Task VisitAsync(string route)
    {
        await GoToAsync(webUrl + route);
        await new ShellPage(Page).WaitForShellAsync();
    }

    public Task SaveScreenshotAsync(string path) => Page.ScreenshotAsync(new() { Path = path, FullPage = true });
}
