// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class ClientAddressClaimTests
{
    private const string BrowserAddress = "203.0.113.5";
    private const string OtherAddress = "198.51.100.9";
    private const string ForwardedAddress = "6.6.6.6";
    private const string GatewayAddress = "7.7.7.7";

    [Fact]
    public void AddTo_Address_IsReadBack()
    {
        var principal = AuthenticatedPrincipal();

        ClientAddressClaim.AddTo(principal, IPAddress.Parse(BrowserAddress));

        Assert.Equal(BrowserAddress, ClientAddressClaim.Read(principal));
    }

    [Fact]
    public void AddTo_AddedIdentity_IsNotAuthenticatedAndNotPrimary()
    {
        var principal = AuthenticatedPrincipal();
        var original = principal.Identity;

        ClientAddressClaim.AddTo(principal, IPAddress.Parse(BrowserAddress));

        var added = Assert.Single(principal.Identities, identity => !ReferenceEquals(identity, original));
        Assert.False(added.IsAuthenticated);
        Assert.Same(original, principal.Identity);
        Assert.True(principal.Identity!.IsAuthenticated);
    }

    [Fact]
    public void AddTo_PrincipalAlreadyCarriesTheClaim_ReplacesIt()
    {
        var principal = AuthenticatedPrincipal();
        principal.Identities.First().AddClaim(new Claim(ClientAddressClaim.Type, OtherAddress));

        ClientAddressClaim.AddTo(principal, IPAddress.Parse(BrowserAddress));

        Assert.Equal(BrowserAddress, ClientAddressClaim.Read(principal));
        Assert.Single(principal.FindAll(ClientAddressClaim.Type));
    }

    [Fact]
    public void AddTo_NullAddressAndExistingClaim_RemovesIt()
    {
        var principal = AuthenticatedPrincipal();
        principal.Identities.First().AddClaim(new Claim(ClientAddressClaim.Type, OtherAddress));

        ClientAddressClaim.AddTo(principal, null);

        Assert.Null(ClientAddressClaim.Read(principal));
        Assert.Empty(principal.FindAll(ClientAddressClaim.Type));
    }

    [Fact]
    public void AddTo_Ipv4MappedAddress_IsUnmapped()
    {
        var principal = AuthenticatedPrincipal();

        ClientAddressClaim.AddTo(principal, IPAddress.Parse("::ffff:203.0.113.5"));

        Assert.Equal(BrowserAddress, ClientAddressClaim.Read(principal));
    }

    [Fact]
    public void Read_TwoClaims_ReturnsNull()
    {
        var principal = AuthenticatedPrincipal();
        principal.Identities.First().AddClaim(new Claim(ClientAddressClaim.Type, BrowserAddress));
        principal.Identities.First().AddClaim(new Claim(ClientAddressClaim.Type, OtherAddress));

        Assert.Null(ClientAddressClaim.Read(principal));
    }

    [Fact]
    public async Task Capture_AnonymousRequest_AddsNoIdentity()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(BrowserAddress);
        var identitiesBefore = context.User.Identities.Count();

        await RunCaptureAsync(context);

        Assert.DoesNotContain(context.User.Identities, identity => identity.IsAuthenticated);
        Assert.DoesNotContain(context.User.Claims, claim => claim.Type == ClientAddressClaim.Type);
        Assert.Equal(identitiesBefore, context.User.Identities.Count());
    }

    [Fact]
    public async Task Capture_RequestCarriesForwardedAndGatewayHeaders_UsesTheConnectionAddress()
    {
        var context = new DefaultHttpContext
        {
            User = AuthenticatedPrincipal(),
        };
        context.Connection.RemoteIpAddress = IPAddress.Parse(BrowserAddress);
        context.Request.Headers["X-Forwarded-For"] = ForwardedAddress;
        context.Request.Headers["X-Real-IP"] = ForwardedAddress;
        context.Request.Headers["Forwarded"] = $"for={ForwardedAddress}";
        context.Request.Headers["X-SharpVault-Client-Ip"] = GatewayAddress;

        await RunCaptureAsync(context);

        Assert.Equal(BrowserAddress, ClientAddressClaim.Read(context.User));
    }

    private static ClaimsPrincipal AuthenticatedPrincipal() => new(new ClaimsIdentity("Test"));

    private static async Task RunCaptureAsync(HttpContext context)
    {
        var builder = WebApplication.CreateBuilder();
        await using var app = builder.Build();
        app.UseClientAddressCapture();
        app.Run(_ => Task.CompletedTask);
        var pipeline = ((IApplicationBuilder)app).Build();
        await pipeline(context);
    }
}
