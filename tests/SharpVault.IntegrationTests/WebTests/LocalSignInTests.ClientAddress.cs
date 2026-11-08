// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.WebGateway;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Support;
using WebApp::SharpVault.Web.Security;

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

    [Fact]
    public async Task ClientAddress_BrowserSendsForwardedAndGatewayHeaders_RowHoldsTheConnectionAddress()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await _api.ResetAndSeedAsync(cancellationToken);
        using var client = _web.CreateWebClient();
        var session = await SignInAsync(client, LocalAccounts.UserOne);
        client.DefaultRequestHeaders.Add(LoopbackStartupFilter.RemoteAddressHeader, TestAddresses.Browser);
        client.DefaultRequestHeaders.Add(ClientAddressProofs.ForwardedForHeader, ClientAddressProofs.ForwardedValue);
        client.DefaultRequestHeaders.Add(ClientAddressProofs.RealIpHeader, ClientAddressProofs.ForwardedValue);
        client.DefaultRequestHeaders.Add(ClientAddressProofs.ForwardedHeader, $"for={ClientAddressProofs.ForwardedValue}");
        client.DefaultRequestHeaders.Add(WebGatewayHeaders.ClientIp, ClientAddressProofs.ForwardedValue);
        client.DefaultRequestHeaders.Add(WebGatewayHeaders.Signature, ClientAddressProofs.JunkValue);

        var response = await PostSignOutAsync(client, session, withToken: true);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var table = _api.StorageAdminClient.GetTableClient(TableNames.AuditLog);
        var rows = await AuditRows.ListAsync(table, cancellationToken);
        var signIn = Assert.Single(rows, row =>
            row.GetString("EventType") == "SignIn" &&
            row.GetString("ActorId") == LocalAccounts.UserOne.Id.ToString("D"));
        Assert.Equal(TestAddresses.Browser, signIn.GetString("IpAddress"));
    }

    [Fact]
    public async Task PostPicker_SessionCookieTicket_HasNoAddressClaim()
    {
        using var client = _web.CreateWebClient();

        var response = await PostAsync(client, LocalAccounts.UserOne.Id.ToString("D"), "/");

        var principal = Unprotect(CookieValue(response)).Principal;
        Assert.NotNull(principal);
        Assert.DoesNotContain(principal.Claims, claim => claim.Type == ClientAddressClaim.Type);
    }
}
