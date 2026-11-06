// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Pages;

public sealed class SharedSecretPage(IPage page) : PageObject(page)
{
    private const string AccessEndedAlertId = "shared-secret-detail-error";
    private const string AccessEndedText = "Your access to this item has ended.";

    public Task AssertAccessEndedAsync() => ContainsTextAsync(AccessEndedAlertId, AccessEndedText);
}
