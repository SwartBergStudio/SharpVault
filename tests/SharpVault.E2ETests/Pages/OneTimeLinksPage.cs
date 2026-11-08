// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class OneTimeLinksPage(IPage page, string webUrl) : PageObject(page)
{
    private const string HeadingId = "one-time-links-heading";
    private const string NoticePrefix = "one-time-link-notice-";

    public async Task OpenAsync()
    {
        await GoToAsync(webUrl + Routes.OneTimeLinks);
        await ShowsAsync(HeadingId);
    }

    public Task AssertNoticeForLinkAsync(Guid linkId) => ShowsAsync(NoticePrefix + linkId.ToString("D"));
}
