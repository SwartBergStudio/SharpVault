// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class LocalSignInPage(IPage page, string webUrl) : PageObject(page)
{
    private const string AccountListId = "local-account-list";
    private const string AccountIdPrefix = "local-account-";
    private const string SlugSeparator = "-";

    public async Task OpenAsync()
    {
        await GoToAsync(webUrl + Routes.LocalSignIn);
        await ShowsAsync(AccountListId);
    }

    public async Task SignInAsAsync(string displayName)
    {
        var slug = displayName.ToLowerInvariant().Replace(" ", SlugSeparator, StringComparison.Ordinal);
        await ClickAsync(AccountIdPrefix + slug);
        await WaitForUrlNotAsync(Routes.LocalSignIn);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = TimeoutMilliseconds });
    }
}
