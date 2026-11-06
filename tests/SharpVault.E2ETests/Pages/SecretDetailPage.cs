// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Pages;

public sealed class SecretDetailPage(IPage page) : PageObject(page)
{
    private const string SecretHeadingId = "secret-heading";
    private const string RevealValueId = "secret-value-reveal";
    private const string ValueId = "secret-value";
    private const string RevealNotesId = "secret-notes-reveal";
    private const string NotesId = "secret-notes";

    public async Task WaitForAsync() => await ShowsAsync(SecretHeadingId);

    public Guid SecretId()
    {
        var path = new Uri(Page.Url).AbsolutePath;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Guid.Parse(segments[^1] == "edit" || segments[^1] == "history" ? segments[^2] : segments[^1]);
    }

    public async Task RevealValueAsync(string expected)
    {
        await ClickAsync(RevealValueId);
        await ContainsTextAsync(ValueId, expected);
    }

    public async Task RevealNotesAsync(string expected)
    {
        await ClickAsync(RevealNotesId);
        await ContainsTextAsync(NotesId, expected);
    }
}
