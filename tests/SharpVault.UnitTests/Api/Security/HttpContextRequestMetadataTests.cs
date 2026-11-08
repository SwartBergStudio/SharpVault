// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpVault.Api.Security;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.UnitTests.Api.Security;

public sealed class HttpContextRequestMetadataTests
{
    private sealed class FixedAccessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static ClientAddressResolver Resolver() =>
        new(Options.Create(new WebGatewayOptions()), TimeProvider.System, NullLogger<ClientAddressResolver>.Instance);

    private static HttpContextRequestMetadata For(IPAddress? address, Action<HttpContext>? configure = null)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = address;
        configure?.Invoke(context);
        return new HttpContextRequestMetadata(new FixedAccessor(context), Resolver());
    }

    [Theory]
    [InlineData("10.1.2.3", "10.1.2.3")]
    [InlineData("2001:DB8:0:0:0:0:0:1", "2001:db8::1")]
    [InlineData("::1", "::1")]
    public void IpAddress_FromConnection_IsNormalized(string address, string expected)
    {
        Assert.Equal(expected, For(IPAddress.Parse(address)).IpAddress);
    }

    [Fact]
    public void IpAddress_NoContext_IsNull()
    {
        Assert.Null(new HttpContextRequestMetadata(new FixedAccessor(null), Resolver()).IpAddress);
    }

    [Fact]
    public void IpAddress_NoRemoteAddress_IsNull()
    {
        Assert.Null(For(null).IpAddress);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void IpAddress_ForwardedHeaders_AreIgnored()
    {
        var metadata = For(IPAddress.Parse("10.1.2.3"), context =>
        {
            context.Request.Headers["X-Forwarded-For"] = "6.6.6.6";
            context.Request.Headers["X-Real-IP"] = "7.7.7.7";
            context.Request.Headers["Forwarded"] = "for=8.8.8.8";
        });

        Assert.Equal("10.1.2.3", metadata.IpAddress);
    }
}
