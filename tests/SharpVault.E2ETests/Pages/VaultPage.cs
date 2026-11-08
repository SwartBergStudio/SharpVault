// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class VaultPage(IPage page, string webUrl) : PageObject(page)
{
    private const string BrandLinkId = "brand-home-link";
    private const string UnfiledLinkId = "folder-tree-unfiled";
    private const string NewFolderId = "vault-new-folder";
    private const string FolderNameInputId = "folder-editor-name-input";
    private const string FolderSaveId = "folder-editor-save";
    private const string NewItemId = "vault-new-secret";
    private const string SecretNameInputId = "secret-name-input";
    private const string SecretTypeSelectId = "secret-type-select";
    private const string SecretUsernameInputId = "secret-username-input";
    private const string SecretValueInputId = "secret-value-input";
    private const string SecretNotesInputId = "secret-notes-input";
    private const string SecretSaveId = "secret-save";
    private const string SecretHeadingId = "secret-heading";
    private const string SecretListId = "vault-secret-list";
    private const string SecretLinkPrefix = "[id^='vault-secret-link-']";
    private const string FolderLinkPrefix = "[id^='folder-link-']";
    private const string FocusInsideFolderEditorScript = "() => document.getElementById('folder-editor-dialog')?.contains(document.activeElement) === true";
    private const string SecureNoteOption = "Secure note";

    public async Task OpenAsync()
    {
        await GoToAsync(webUrl + Routes.Vault);
        await ShowsAsync(NewItemId);
    }

    public async Task FollowBrandLinkFromUnfiledAsync()
    {
        await Element(UnfiledLinkId).ClickAsync();
        await WaitForUrlAsync(url => new Uri(url).AbsolutePath == Routes.Unfiled, Routes.Unfiled);
        await Element(BrandLinkId).ClickAsync();
    }

    public async Task WaitForLandingAsync()
    {
        await WaitForUrlAsync(url => new Uri(url).AbsolutePath == Routes.Vault, Routes.Vault);
        await ShowsAsync(NewItemId);
    }

    public async Task CreateFolderAsync(string name)
    {
        await ClickAsync(NewFolderId);
        await FillAsync(FolderNameInputId, name);
        await ClickAsync(FolderSaveId);
        await HidesAsync(FolderSaveId);
        await ShowsFolderAsync(name);
    }

    public Task AssertNewItemHiddenAsync() => HidesAsync(NewItemId);

    public async Task OpenFolderAsync(string name)
    {
        await ShowsFolderAsync(name);
        try
        {
            await Page.Locator(FolderLinkPrefix).Filter(new() { HasText = name }).First.ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not open folder \"{name}\" at {Page.Url}: {exception.Message}", exception);
        }

        await WaitForUrlAsync(url => url.Contains(Routes.Vault + "/folders/", StringComparison.Ordinal), "a folder URL");
    }

    public async Task CreateLoginAsync(string name, string username, string value)
    {
        await NewItemAsync();
        await FillAsync(SecretNameInputId, name);
        await FillAsync(SecretUsernameInputId, username);
        await FillAsync(SecretValueInputId, value);
        await SaveItemAsync();
    }

    public async Task CreateSecureNoteAsync(string name, string notes)
    {
        await NewItemAsync();
        await FillAsync(SecretNameInputId, name);
        await ChooseOptionAsync(SecretTypeSelectId, SecureNoteOption);
        await FillAsync(SecretNotesInputId, notes);
        await SaveItemAsync();
    }

    public async Task OpenItemAsync(string name)
    {
        try
        {
            await Element(SecretListId).Locator(SecretLinkPrefix).Filter(new() { HasText = name }).First.ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"Could not open \"{name}\" at {Page.Url}: {exception.Message}", exception);
        }
    }

    public Task AssertListShowsAsync(string name) => ListContainsAsync(name);

    private async Task ShowsFolderAsync(string name)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Page.Locator(FolderLinkPrefix).Filter(new() { HasText = name }).First).ToBeVisibleAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"The folder \"{name}\" did not show in the tree at {Page.Url}: {exception.Message}", exception);
        }
    }

    public async Task OpenNewFolderEditorAsync()
    {
        await ClickAsync(NewFolderId);
        await ShowsAsync(FolderNameInputId);
    }

    public Task WaitForFolderEditorFocusAsync() =>
        Page.WaitForFunctionAsync(FocusInsideFolderEditorScript, null, new() { Timeout = TimeoutMilliseconds });

    public Task PressEscapeAsync() => Page.Keyboard.PressAsync("Escape");

    public Task AssertFolderEditorHiddenAsync() => HidesAsync(FolderNameInputId);

    private async Task NewItemAsync()
    {
        await ClickAsync(NewItemId);
        await ShowsAsync(SecretNameInputId);
    }

    private async Task SaveItemAsync()
    {
        await ClickAsync(SecretSaveId);
        await ShowsAsync(SecretHeadingId);
    }

    private async Task ListContainsAsync(string name)
    {
        try
        {
            await Microsoft.Playwright.Assertions.Expect(Element(SecretListId)).ToContainTextAsync(name, new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"\"{name}\" did not appear in the item list at {Page.Url}: {exception.Message}", exception);
        }
    }
}
