// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class OneTimeLinkOpenPage(IPage page) : PageObject(page)
{
    private const string OpenButtonId = "otl-open-button";
    private const string PassphraseFormId = "otl-passphrase-submit";
    private const string PassphraseInputId = "otl-passphrase-input";
    private const string WrongPassphraseId = "otl-passphrase-wrong";
    private const string UnavailableId = "otl-unavailable";
    private const string ValueId = "otl-value";
    private const string ValueRowId = "otl-row-value";
    private const string FragmentMarker = "#";

    public async Task OpenAsync(string linkUrl)
    {
        await GoToAsync(linkUrl);
        await ShowsAsync(OpenButtonId);
    }

    public async Task ClickOpenAsync()
    {
        await ClickAsync(OpenButtonId);
    }

    public async Task AssertPassphraseFormShownAsync()
    {
        await ShowsAsync(PassphraseFormId);
    }

    public Task TypePassphraseAsync(string passphrase) =>
        Element(PassphraseInputId).PressSequentiallyAsync(passphrase, new() { Timeout = TimeoutMilliseconds });

    public async Task SubmitPassphraseAsync(string passphrase)
    {
        await FillAsync(PassphraseInputId, passphrase);
        await ClickAsync(PassphraseFormId);
    }

    public Task AssertWrongPassphraseAsync() => ShowsAsync(WrongPassphraseId);

    public async Task AssertValueAsync(string expected)
    {
        await ShowsAsync(ValueRowId);
        await HasExactTextAsync(ValueId, expected);
    }

    public Task AssertUnavailableAsync() => ShowsAsync(UnavailableId);

    public async Task ReloadAsync()
    {
        await Page.ReloadAsync(new() { WaitUntil = WaitUntilState.DOMContentLoaded, Timeout = TimeoutMilliseconds });
    }

    public Task AssertAddressBarHasNoFragmentAsync()
    {
        Assert.DoesNotContain(FragmentMarker, Page.Url, StringComparison.Ordinal);
        return Task.CompletedTask;
    }
}
