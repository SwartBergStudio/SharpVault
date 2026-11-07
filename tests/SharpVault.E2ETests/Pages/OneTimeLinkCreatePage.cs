// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class OneTimeLinkCreatePage(IPage page, string webUrl) : PageObject(page)
{
    private const string NewLinkButtonId = "otl-new-link";
    private const string CreateButtonId = "otl-create";
    private const string PassphraseInputId = "otl-passphrase";
    private const string ResultId = "otl-result";
    private const string CreateStatusId = "otl-create-status";
    private const string LinkOutputId = "otl-link-output";

    public async Task OpenFromSecretPageAsync()
    {
        await ShowsAsync(NewLinkButtonId);
        await ClickAsync(NewLinkButtonId);
        await ShowsAsync(CreateButtonId);
    }

    public async Task OpenAsync(Guid ownerId, Guid secretId)
    {
        await GoToAsync(webUrl + Routes.OneTimeLinkNewPrefix + ownerId.ToString("D") + "/" + secretId.ToString("D"));
        await ShowsAsync(CreateButtonId);
    }

    public Task SetPassphraseAsync(string passphrase) => FillAsync(PassphraseInputId, passphrase);

    public Task TypePassphraseAsync(string passphrase) =>
        Element(PassphraseInputId).PressSequentiallyAsync(passphrase, new() { Timeout = TimeoutMilliseconds });

    public Task<string> PassphraseValueAsync() => Element(PassphraseInputId).InputValueAsync();

    public Task ClickCreateAsync() => ClickAsync(CreateButtonId);

    public Task ShowsStatusAsync(string text) => ContainsTextAsync(CreateStatusId, text);

    public async Task<string> CreateLinkAsync()
    {
        await ClickAsync(CreateButtonId);
        await ShowsAsync(ResultId);
        var link = await Element(LinkOutputId).InputValueAsync();
        Assert.Matches(@"/l/[0-9a-f-]{36}#", link);
        return link;
    }
}
