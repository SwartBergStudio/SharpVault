// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public sealed class SharePanelPage(IPage page) : PageObject(page)
{
    private const string AddId = "share-add";
    private const string SearchInputId = "recipient-search-input";
    private const string SearchSubmitId = "recipient-search-submit";
    private const string ConfirmId = "share-dialog-confirm";
    private const string RevokeConfirmId = "share-revoke-prompt-confirm";
    private const string ResultPrefix = "recipient-result-";
    private const string GrantNamePrefix = "share-grant-name-";
    private const string GrantRevokePrefix = "share-grant-revoke-";

    public async Task ShareWithAsync(string searchText, Guid recipientId, string recipientName)
    {
        await ClickAsync(AddId);
        await FillAsync(SearchInputId, searchText);
        await ClickAsync(SearchSubmitId);
        await ClickResultAsync(recipientId);
        await ClickAsync(ConfirmId);
        await HidesAsync(ConfirmId);
        await ContainsTextAsync(GrantNamePrefix + recipientId.ToString("D"), recipientName);
    }

    public async Task RevokeAsync(Guid recipientId)
    {
        await ClickAsync(GrantRevokePrefix + recipientId.ToString("D"));
        await ClickAsync(RevokeConfirmId);
        await HidesAsync(RevokeConfirmId);
    }

    private async Task ClickResultAsync(Guid recipientId)
    {
        try
        {
            await Element(ResultPrefix + recipientId.ToString("D")).ClickAsync(new() { Timeout = TimeoutMilliseconds });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            throw new InvalidOperationException($"The recipient {recipientId:D} was not in the search results at {Page.Url}: {exception.Message}", exception);
        }
    }
}
