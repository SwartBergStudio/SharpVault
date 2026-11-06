// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public sealed class FolderTreePage(IPage page) : PageObject(page)
{
    private const string FolderLinkPrefix = "[id^='folder-link-']";
    private const string FolderLinkIdPrefix = "folder-link-";
    private const string FolderActionsIdPrefix = "folder-actions-";
    private const string NewChildIdPrefix = "folder-new-child-";
    private const string FolderNameInputId = "folder-editor-name-input";
    private const string FolderSaveId = "folder-editor-save";

    public async Task CreateChildFolderAsync(string parentName, string childName)
    {
        var parentId = await FolderIdAsync(parentName);
        await OpenActionsAsync(parentId);
        await ClickAsync(NewChildIdPrefix + parentId);
        await FillAsync(FolderNameInputId, childName);
        await ClickAsync(FolderSaveId);
        await HidesAsync(FolderSaveId);
        await WaitForFolderAsync(childName);
    }

    public async Task WaitForFolderAsync(string name)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(FolderLink(name)).ToBeVisibleAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"The folder \"{name}\" did not show in the tree at {Page.Url}: {exception.Message}", exception);
        }
    }

    private ILocator FolderLink(string name) => Page.Locator(FolderLinkPrefix).Filter(new() { HasText = name }).First;

    private async Task<string> FolderIdAsync(string name)
    {
        await WaitForFolderAsync(name);
        var id = await FolderLink(name).GetAttributeAsync("id") ?? throw new InvalidOperationException($"The folder \"{name}\" has no id at {Page.Url}.");
        return id[FolderLinkIdPrefix.Length..];
    }

    private async Task OpenActionsAsync(string folderId)
    {
        try
        {
            await Element(FolderActionsIdPrefix + folderId).Locator("button").First.ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not open the actions for folder {folderId} at {Page.Url}: {exception.Message}", exception);
        }
    }

}
