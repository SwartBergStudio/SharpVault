// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.Api.Security;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Users.Access;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Security;

/// <summary>
/// TEN-3, TEN-4 and LCK-1: guests, roles and the lock check run in that order, and the gate runs once per request.
/// </summary>
[Trait("Category", "Security")]
public sealed class SharpVaultAccessHandlerTests
{
    private static readonly SharpVaultAccessRequirement UserRequirement = new([AppRoles.User, AppRoles.Admin]);
    private static readonly SharpVaultAccessRequirement AdminRequirement = new([AppRoles.Admin]);

    private readonly IUserAccessGate _gate = Substitute.For<IUserAccessGate>();
    private readonly StubGuestAccessPolicy _guestPolicy = new();
    private readonly RecordingLogger<SharpVaultAccessHandler> _logger = new();
    private readonly SharpVaultAccessHandler _handler;
    private readonly Guid _userId = Guid.NewGuid();

    public SharpVaultAccessHandlerTests()
    {
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>()).Returns(UserAccessDecision.Allowed);
        _handler = new SharpVaultAccessHandler(_gate, _guestPolicy, TestIdentities.Settings, _logger);
    }

    [Fact]
    public async Task HandleRequirementAsync_Anonymous_NeitherSucceedsNorCallsGate()
    {
        var context = Context(TestIdentities.Anonymous(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        Assert.False(context.HasFailed);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_NoIdentityAtAll_NeitherSucceedsNorCallsGate()
    {
        var context = Context(new ClaimsPrincipal(), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_InvalidObjectId_FailsWithoutCallingGate()
    {
        var claims = TestIdentities.MemberClaims(_userId, AppRoles.User).Replacing("oid", "not-a-guid");
        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
        await AssertGateNotCalled();
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData("0", "live.com")]
    [InlineData("2", null)]
    [InlineData(null, null)]
    public async Task HandleRequirementAsync_Guest_FailsWithoutCallingGate(string? acct, string? idp)
    {
        var claims = TestIdentities.MemberClaims(_userId, AppRoles.User).Without("acct");
        if (acct is not null)
        {
            claims.Add(new Claim("acct", acct));
        }

        if (idp is not null)
        {
            claims.Add(new Claim("idp", idp));
        }

        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_GuestAllowedWithRole_CallsGate()
    {
        _guestPolicy.GuestsAllowed = true;
        var claims = TestIdentities.MemberClaims(_userId, AppRoles.User).Replacing("acct", "1");
        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        await _gate.Received(1).CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_GuestAllowedButNoRole_FailsWithoutCallingGate()
    {
        _guestPolicy.GuestsAllowed = true;
        var claims = TestIdentities.MemberClaims(_userId).Replacing("acct", "1");
        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_MemberDoesNotAskGuestPolicy()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.Empty(_guestPolicy.Tokens);
    }

    [Fact]
    public async Task HandleRequirementAsync_MissingRole_FailsWithoutCallingGate()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await AssertGateNotCalled();
    }

    [Theory]
    [InlineData("role", "SharpVault.Admin")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SharpVault.User")]
    [InlineData("roles", "sharpvault.user")]
    [InlineData("roles", "SharpVault.User ")]
    [InlineData("roles", "SharpVault.User,SharpVault.Admin")]
    [InlineData("groups", "SharpVault.Admin")]
    public async Task HandleRequirementAsync_RoleInjectedThroughOtherClaim_Fails(string claimType, string value)
    {
        var claims = TestIdentities.MemberClaims(_userId).With(claimType, value);
        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_AdminRoleForUserPolicy_Succeeds()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.Admin)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_UserRoleForUserPolicy_Succeeds()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_UserRoleForAdminPolicy_Fails()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), AdminRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
        await AssertGateNotCalled();
    }

    [Fact]
    public async Task HandleRequirementAsync_AdminRoleForAdminPolicy_Succeeds()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.Admin)), AdminRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_GateDenied_Fails()
    {
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>()).Returns(UserAccessDecision.Denied);
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.Admin)), AdminRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_GateAllowed_Succeeds()
    {
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        await _gate.Received(1).CheckAsync(Arg.Is<SignedInUser>(user => user.UserId == _userId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_GateThrows_PropagatesInsteadOfSucceeding()
    {
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException("storage down"));
        var context = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await Assert.ThrowsAsync<TimeoutException>(() => _handler.HandleAsync(context));

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task HandleRequirementAsync_TwoRequirementsSameRequest_CallsGateOnce()
    {
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.Admin));
        var context = new AuthorizationHandlerContext([UserRequirement, AdminRequirement], principal, new DefaultHttpContext());

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        await _gate.Received(1).CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_TwoContextsSameRequest_CallsGateOnce()
    {
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User));
        var httpContext = new DefaultHttpContext();

        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, httpContext));
        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, httpContext));

        await _gate.Received(1).CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_DeniedDecisionMemoized_StaysDeniedForSameRequest()
    {
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>())
            .Returns(UserAccessDecision.Denied, UserAccessDecision.Allowed);
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.Admin));
        var httpContext = new DefaultHttpContext();

        var first = new AuthorizationHandlerContext([UserRequirement], principal, httpContext);
        var second = new AuthorizationHandlerContext([AdminRequirement], principal, httpContext);
        await _handler.HandleAsync(first);
        await _handler.HandleAsync(second);

        Assert.True(first.HasFailed);
        Assert.True(second.HasFailed);
    }

    [Fact]
    public async Task HandleRequirementAsync_DifferentRequests_EachCallsGate()
    {
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User));

        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, new DefaultHttpContext()));
        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, new DefaultHttpContext()));

        await _gate.Received(2).CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_ForwardedForHeader_PassesConnectionAddressOnly()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
        httpContext.Request.Headers["X-Forwarded-For"] = "203.0.113.9";
        httpContext.Request.Headers["Forwarded"] = "for=203.0.113.10";
        httpContext.Request.Headers["X-Real-IP"] = "203.0.113.11";
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User));

        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, httpContext));

        await _gate.Received(1).CheckAsync(Arg.Is<SignedInUser>(user => user.IpAddress == "10.0.0.5"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_NoRemoteAddress_PassesNullIpAddress()
    {
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User));

        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], principal, new DefaultHttpContext()));

        await _gate.Received(1).CheckAsync(Arg.Is<SignedInUser>(user => user.IpAddress == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_NonHttpResource_StillCallsGateWithNoToken()
    {
        var principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User));

        var context = new AuthorizationHandlerContext([UserRequirement], principal, resource: null);
        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
        await _gate.Received(1).CheckAsync(Arg.Is<SignedInUser>(user => user.IpAddress == null), CancellationToken.None);
    }

    [Fact]
    public async Task HandleRequirementAsync_PassesRequestAbortedToken()
    {
        using var source = new CancellationTokenSource();
        var httpContext = new DefaultHttpContext { RequestAborted = source.Token };
        var claims = TestIdentities.MemberClaims(_userId, AppRoles.User).Replacing("acct", "1");
        _guestPolicy.GuestsAllowed = true;

        await _handler.HandleAsync(new AuthorizationHandlerContext([UserRequirement], TestIdentities.Authenticated(claims), httpContext));

        await _gate.Received(1).CheckAsync(Arg.Any<SignedInUser>(), source.Token);
        Assert.Equal([source.Token], _guestPolicy.Tokens);
    }

    [Fact]
    public async Task HandleRequirementAsync_PassesClaimValuesToGateUnmodified()
    {
        var claims = TestIdentities.MemberClaims(_userId, AppRoles.User).With("name", "Alice Example").With("email", "alice@example.test");
        var context = Context(TestIdentities.Authenticated(claims), UserRequirement);

        await _handler.HandleAsync(context);

        await _gate.Received(1).CheckAsync(
            Arg.Is<SignedInUser>(user => user.DisplayName == "Alice Example" && user.Email == "alice@example.test"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleRequirementAsync_AllDenials_LogNoNameOrEmail()
    {
        const string name = "Alice Example";
        const string email = "alice@example.test";
        var guest = TestIdentities.MemberClaims(_userId, AppRoles.User).Replacing("acct", "1").With("name", name).With("email", email);
        var noRole = TestIdentities.MemberClaims(_userId).With("name", name).With("email", email);

        await _handler.HandleAsync(Context(TestIdentities.Authenticated(guest), UserRequirement));
        await _handler.HandleAsync(Context(TestIdentities.Authenticated(noRole), UserRequirement));
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>()).Returns(UserAccessDecision.Denied);
        var locked = TestIdentities.MemberClaims(_userId, AppRoles.User).With("name", name).With("email", email);
        await _handler.HandleAsync(Context(TestIdentities.Authenticated(locked), UserRequirement));

        Assert.NotEmpty(_logger.Records);
        Assert.DoesNotContain(name, _logger.AllText(), StringComparison.Ordinal);
        Assert.DoesNotContain(email, _logger.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleRequirementAsync_DenialReasons_AreIdenticalForNoRoleAndLockedFailureShape()
    {
        var noRole = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId)), UserRequirement);
        _gate.CheckAsync(Arg.Any<SignedInUser>(), Arg.Any<CancellationToken>()).Returns(UserAccessDecision.Denied);
        var locked = Context(TestIdentities.Authenticated(TestIdentities.MemberClaims(_userId, AppRoles.User)), UserRequirement);

        await _handler.HandleAsync(noRole);
        await _handler.HandleAsync(locked);

        Assert.True(noRole.HasFailed);
        Assert.True(locked.HasFailed);
        Assert.Single(noRole.FailureReasons);
        Assert.Single(locked.FailureReasons);
    }

    private static AuthorizationHandlerContext Context(ClaimsPrincipal principal, params IAuthorizationRequirement[] requirements) =>
        new(requirements, principal, new DefaultHttpContext());

    private async Task AssertGateNotCalled()
    {
        await _gate.DidNotReceiveWithAnyArgs().CheckAsync(default!, default);
    }
}
