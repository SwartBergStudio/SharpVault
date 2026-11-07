// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.E2ETests.Hosting;
using SharpVault.E2ETests.Pages;
using SharpVault.E2ETests.Support;

namespace SharpVault.E2ETests.Journeys;

[Collection(nameof(BrowserAddressGroup))]
[Trait("Category", "E2E")]
public sealed class BrowserAddressTests(BrowserAddressHost host)
{
    private const string BrowserLoopbackAddress = "::1";
    private const string EventTypeColumn = "EventType";
    private const string ActorIdColumn = "ActorId";
    private const string IpAddressColumn = "IpAddress";
    private const string SignInEvent = "SignIn";
    private const string RevealEvent = "SecretValueRevealed";
    private const string UsernameValue = "user.one@sharpvault.invalid";

    [Fact]
    public async Task ClientAddress_RevealInABrowserOverIpv6Loopback_RowsHoldBrowserAddressNotWebHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var loginName = "0 Login " + suffix;
        var valueMarker = "value-marker-" + suffix;

        await using var session = await host.OpenSessionAsync();
        var page = await session.NewPageAsync();
        var signIn = new LocalSignInPage(page, host.Ipv6WebUrl);
        await signIn.OpenAsync();
        await signIn.SignInAsAsync(E2EAccounts.UserOne);

        var vault = new VaultPage(page, host.Ipv6WebUrl);
        await vault.OpenAsync();
        await vault.CreateLoginAsync(loginName, UsernameValue, valueMarker);
        await new SecretDetailPage(page).RevealValueAsync(valueMarker);

        var rows = await E2EAuditRows.ListAsync(host.StorageConnectionString, cancellationToken);
        var reveal = rows.First(row => row.GetString(EventTypeColumn) == RevealEvent && row.GetString(ActorIdColumn) == E2EAccounts.UserOneId.ToString("D"));
        Assert.Equal(BrowserLoopbackAddress, reveal.GetString(IpAddressColumn));
        var sign = rows.First(row => row.GetString(EventTypeColumn) == SignInEvent && row.GetString(ActorIdColumn) == E2EAccounts.UserOneId.ToString("D"));
        Assert.Equal(BrowserLoopbackAddress, sign.GetString(IpAddressColumn));
    }
}
