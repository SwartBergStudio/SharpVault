// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class SharedWithMePage(IPage page, string webUrl) : PageObject(page)
{
    private const string HeadingId = "shared-heading";
    private const string OwnerItemLinks = "[id^='shared-item-link-']";

    public async Task OpenAsync()
    {
        await GoToAsync(webUrl + Routes.Shared);
        await ShowsAsync(HeadingId);
    }

    public async Task AssertItemUnderOwnerAsync(Guid ownerId, string itemName)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element("shared-owner-" + ownerId.ToString("D"))).ToContainTextAsync(itemName, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"\"{itemName}\" was not listed under the owner {ownerId:D} at {Page.Url}: {exception.Message}", exception);
        }
    }

    public async Task OpenItemAsync(Guid ownerId, string itemName)
    {
        try
        {
            await Element("shared-owner-table-" + ownerId.ToString("D")).Locator(OwnerItemLinks).Filter(new() { HasText = itemName }).First.ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not open \"{itemName}\" at {Page.Url}: {exception.Message}", exception);
        }
    }
}
