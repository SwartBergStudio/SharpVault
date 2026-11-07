// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using Microsoft.AspNetCore.Http;
using SharpVault.Authentication.Mode;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class LocalSignInGuardTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("[::1]")]
    public void Allows_LocalModeLoopbackLocalhost_ReturnsTrue(string host)
    {
        Assert.True(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Loopback, host, hasForwardedHeaders: false));
    }

    [Fact]
    public void Allows_EntraMode_ReturnsFalse()
    {
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Entra, IPAddress.Loopback, "localhost", hasForwardedHeaders: false));
    }

    [Theory]
    [InlineData("203.0.113.9")]
    [InlineData("2001:db8::1")]
    public void Allows_RemoteAddress_ReturnsFalse(string address)
    {
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Parse(address), "localhost", hasForwardedHeaders: false));
    }

    [Fact]
    public void Allows_NullRemote_ReturnsFalse()
    {
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Local, null, "localhost", hasForwardedHeaders: false));
    }

    [Theory]
    [InlineData("sharpvault.example.com")]
    [InlineData(null)]
    public void Allows_PublicHostHeader_ReturnsFalse(string? host)
    {
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Loopback, host, hasForwardedHeaders: false));
    }

    [Theory]
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-For")]
    [InlineData("X-Forwarded-Host")]
    [InlineData("X-Original-Host")]
    [InlineData("X-Real-IP")]
    public void Allows_EachForwardedHeader_ReturnsFalse(string header)
    {
        var headers = new HeaderDictionary { [header] = "203.0.113.9" };

        var forwarded = LocalSignInGuard.HasForwardedHeaders(headers);

        Assert.True(forwarded);
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Loopback, "localhost", forwarded));
    }

    [Fact]
    public void HasForwardedHeaders_NoForwardedHeader_ReturnsFalse()
    {
        Assert.False(LocalSignInGuard.HasForwardedHeaders(new HeaderDictionary { ["Accept"] = "text/html" }));
    }

    [Theory]
    [InlineData("localhost:5000")]
    [InlineData("[::1]:5000")]
    public void Allows_HostWithPort_IsAccepted(string host)
    {
        Assert.True(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Loopback, host, hasForwardedHeaders: false));
    }

    [Theory]
    [InlineData("localhost.evil.example")]
    [InlineData("localhostevil")]
    [InlineData("127.0.0.1.evil.example")]
    public void Allows_HostLookalikeLocalhostEvil_ReturnsFalse(string host)
    {
        Assert.False(LocalSignInGuard.Allows(AuthenticationMode.Local, IPAddress.Loopback, host, hasForwardedHeaders: false));
    }
}
