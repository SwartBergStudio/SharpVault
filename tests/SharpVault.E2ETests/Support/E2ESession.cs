// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Playwright;

namespace SharpVault.E2ETests.Support;

public sealed class E2ESession(IBrowserContext context)
{
    public const string CspBindingName = "svReportCsp";

    private const string CspListenerScript =
        "document.addEventListener('securitypolicyviolation', (event) => {" +
        $" if (window.{CspBindingName}) {{ window.{CspBindingName}(event.violatedDirective + ' ' + event.blockedURI); }}" +
        " });";

    public PageErrors Errors { get; } = new();

    public IBrowserContext Context { get; } = context;

    public async Task InitializeAsync()
    {
        await Context.ExposeFunctionAsync(CspBindingName, (string violation) => Errors.AddCspViolation(violation));
        await Context.AddInitScriptAsync(CspListenerScript);
    }

    public async Task<IPage> NewPageAsync()
    {
        var page = await Context.NewPageAsync();
        Errors.Attach(page);
        return page;
    }

    public async ValueTask DisposeAsync() => await Context.CloseAsync();
}
