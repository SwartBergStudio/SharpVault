// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Authentication.Local;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;

namespace SharpVault.IntegrationTests.WebTests;

public sealed partial class LocalSignInTests
{
    [Fact]
    public async Task ClientAddress_SignOutThroughWebHost_SignInRowHoldsBrowserAddressNotWebHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.ResetAndSeedAsync(cancellationToken);
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        client.DefaultRequestHeaders.Add(LoopbackStartupFilter.RemoteAddressHeader, TestAddresses.Browser);

        var response = await PostSignOutAsync(client, session, withToken: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var table = _api.StorageAdminClient.GetTableClient(TableNames.AuditLog);
        var rows = await AuditRows.ListAsync(table, cancellationToken);
        var signIn = Assert.Single(rows, row =>
            row.GetString("EventType") == "SignIn" &&
            row.GetString("ActorId") == LocalAccounts.UserOne.Id.ToString("D"));
        Assert.Equal(TestAddresses.Browser, signIn.GetString("IpAddress"));
    }
}
