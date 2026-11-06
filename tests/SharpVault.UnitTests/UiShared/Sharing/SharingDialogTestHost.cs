// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Bunit;
using Microsoft.AspNetCore.Components.Web;
using MudBlazor;

namespace SharpVault.UnitTests.UiShared.Sharing;

public abstract class SharingDialogTestHost : SharingTestHost
{
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    protected static async Task PollAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + PollTimeout;
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, Xunit.TestContext.Current.CancellationToken);
        }
    }

    protected Task PressEscapeAsync() => Provider.Find(".sv-dialog__keys").KeyDownAsync(new KeyboardEventArgs { Key = "Escape" });
}
