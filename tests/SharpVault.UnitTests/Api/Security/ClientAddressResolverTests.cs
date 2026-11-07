// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Api.Security;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.WebGateway;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Security;

[Trait("Category", "Security")]
public sealed class ClientAddressResolverTests
{
    private const string Key = "0123456789abcdef0123456789abcdef";
    private const string OtherKey = "fedcba9876543210fedcba9876543210";
    private const string ConnectionAddress = "10.0.0.1";
    private const string ProofAddress = "198.51.100.7";
    private const string ForgedAddress = "6.6.6.6";
    private const long UnixSeconds = 1_800_000_000;
    private const int WindowSeconds = 60;

    private static readonly Guid UserId = Guid.Parse("6f1c2b3a-0000-4000-8000-000000000001");
    private static readonly Guid OtherUserId = Guid.Parse("6f1c2b3a-0000-4000-8000-000000000002");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(UnixSeconds);

    [Fact]
    public void Resolve_NoHeaders_ReturnsTheConnectionAddressAndLogsNothing()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Resolve_ValidProof_ReturnsTheProofAddress()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);

        Assert.Equal(ProofAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Resolve_InvalidProof_ReturnsTheConnectionAddressAndLogsAWarning()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ForgedAddress, UnixSeconds, OtherKey, UserId);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Resolve_KeyNotConfigured_ReturnsTheConnectionAddressAndLogsAWarning()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);

        Assert.Equal(ConnectionAddress, ResolverFor(key: null, logger).Resolve(context));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Resolve_NoUserOnTheContext_ReturnsTheConnectionAddress()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, user: null);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Resolve_NoConnectionAddressAndInvalidProof_ReturnsNull()
    {
        var context = ContextFor(connection: null, UserId);
        AddProof(context, ForgedAddress, UnixSeconds, OtherKey, UserId);

        Assert.Null(ResolverFor(Key, new CollectingLogger<ClientAddressResolver>()).Resolve(context));
    }

    [Fact]
    public void Resolve_OversizedAddress_ReturnsTheConnectionAddress()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var oversized = new string('1', UserProfileText.IpAddressMaxLength + 1);
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, oversized, UnixSeconds, Key, UserId);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Resolve_OversizedSignature_ReturnsTheConnectionAddress()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);
        context.Request.Headers[WebGatewayHeaders.Signature] = new string('A', 4096);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
    }

    [Fact]
    public void Resolve_ProofOneSecondOutsideTheWindow_ReturnsTheConnectionAddress()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);
        var timeProvider = new FakeTimeProvider(Now.AddSeconds(WindowSeconds + 1));

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger, timeProvider).Resolve(context));
    }

    [Fact]
    public void Resolve_CalledTwiceInARequest_LogsOnce()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ForgedAddress, UnixSeconds, OtherKey, UserId);
        var resolver = ResolverFor(Key, logger);

        Assert.Equal(ConnectionAddress, resolver.Resolve(context));
        Assert.Equal(ConnectionAddress, resolver.Resolve(context));
        Assert.Single(logger.Entries);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Resolve_AnyOutcome_NeverLogsHeaderValues()
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var resolver = ResolverFor(Key, logger);
        var signatures = new List<string>();

        var valid = ContextFor(ConnectionAddress, UserId);
        signatures.Add(AddProof(valid, ProofAddress, UnixSeconds, Key, UserId));
        resolver.Resolve(valid);

        var wrongKey = ContextFor(ConnectionAddress, UserId);
        signatures.Add(AddProof(wrongKey, ForgedAddress, UnixSeconds, OtherKey, UserId));
        resolver.Resolve(wrongKey);

        var oversized = ContextFor(ConnectionAddress, UserId);
        signatures.Add(AddProof(oversized, new string('2', UserProfileText.IpAddressMaxLength + 1), UnixSeconds, Key, UserId));
        resolver.Resolve(oversized);

        var logged = string.Join('\n', logger.Entries.Select(entry => entry.Message));
        Assert.DoesNotContain(ProofAddress, logged);
        Assert.DoesNotContain(ForgedAddress, logged);
        Assert.DoesNotContain(Key, logged);
        Assert.DoesNotContain(OtherKey, logged);
        Assert.All(signatures, signature => Assert.DoesNotContain(signature, logged));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void Resolve_PartialProof_ReturnsTheConnectionAddress(bool time, bool address, bool signature)
    {
        var logger = new CollectingLogger<ClientAddressResolver>();
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);
        RemoveHeaderIf(!time, context, WebGatewayHeaders.Time);
        RemoveHeaderIf(!address, context, WebGatewayHeaders.ClientIp);
        RemoveHeaderIf(!signature, context, WebGatewayHeaders.Signature);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, logger).Resolve(context));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Resolve_ForgedHeadersWithoutSignature_ReturnsTheConnectionAddress()
    {
        var context = ContextFor(ConnectionAddress, UserId);
        context.Request.Headers[WebGatewayHeaders.Time] = UnixSeconds.ToString(CultureInfo.InvariantCulture);
        context.Request.Headers[WebGatewayHeaders.ClientIp] = ForgedAddress;
        context.Request.Headers["X-Forwarded-For"] = ForgedAddress;

        Assert.Equal(ConnectionAddress, ResolverFor(Key, new CollectingLogger<ClientAddressResolver>()).Resolve(context));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Resolve_ProofBoundToAnotherUser_ReturnsTheConnectionAddress()
    {
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, OtherUserId);

        Assert.Equal(ConnectionAddress, ResolverFor(Key, new CollectingLogger<ClientAddressResolver>()).Resolve(context));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Resolve_DuplicateProofHeaders_ReturnsTheConnectionAddress()
    {
        var context = ContextFor(ConnectionAddress, UserId);
        AddProof(context, ProofAddress, UnixSeconds, Key, UserId);
        context.Request.Headers[WebGatewayHeaders.ClientIp] = new StringValues(new[] { ProofAddress, ForgedAddress });

        Assert.Equal(ConnectionAddress, ResolverFor(Key, new CollectingLogger<ClientAddressResolver>()).Resolve(context));
    }

    private static ClientAddressResolver ResolverFor(
        string? key,
        CollectingLogger<ClientAddressResolver> logger,
        FakeTimeProvider? timeProvider = null) =>
        new(
            Options.Create(new WebGatewayOptions { Key = key }),
            timeProvider ?? new FakeTimeProvider(Now),
            logger);

    private static DefaultHttpContext ContextFor(string? connection, Guid? userId = null) =>
        ContextFor(connection, userId is { } id ? UserWith(id) : null);

    private static DefaultHttpContext ContextFor(string? connection, ClaimsPrincipal? user)
    {
        var context = new DefaultHttpContext
        {
            User = user ?? new ClaimsPrincipal(),
        };
        context.Connection.RemoteIpAddress = connection is null ? null : IPAddress.Parse(connection);
        return context;
    }

    private static ClaimsPrincipal UserWith(Guid userId) =>
        new(new ClaimsIdentity([new Claim(EntraClaimTypes.ObjectId, userId.ToString("D"))], "Test"));

    private static string AddProof(HttpContext context, string address, long unixSeconds, string key, Guid userId)
    {
        var signature = WebGatewaySignature.Sign(
            key,
            WebGatewayPurposes.ClientAddress,
            WebGatewayScope.For(userId),
            unixSeconds,
            address,
            string.Empty);
        context.Request.Headers[WebGatewayHeaders.Time] = unixSeconds.ToString(CultureInfo.InvariantCulture);
        context.Request.Headers[WebGatewayHeaders.ClientIp] = address;
        context.Request.Headers[WebGatewayHeaders.Signature] = signature;
        return signature;
    }

    private static void RemoveHeaderIf(bool remove, HttpContext context, string name)
    {
        if (remove)
        {
            context.Request.Headers.Remove(name);
        }
    }
}
