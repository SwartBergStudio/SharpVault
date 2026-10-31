// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NSubstitute;
using SharpVault.Api.Security;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Security;

[Trait("Category", "Security")]
public sealed class HttpContextCurrentUserTests
{
    private readonly IHttpContextAccessor _accessor = Substitute.For<IHttpContextAccessor>();

    private HttpContextCurrentUser UserFor(ClaimsPrincipal? principal)
    {
        _accessor.HttpContext.Returns(principal is null ? null : new DefaultHttpContext { User = principal });
        return new HttpContextCurrentUser(_accessor, TestIdentities.Settings);
    }

    [Fact]
    public void UserId_SignedIn_ReturnsObjectId()
    {
        var oid = Guid.NewGuid();

        var user = UserFor(TestIdentities.Authenticated(TestIdentities.MemberClaims(oid, AppRoles.User).With("email", "other@example.test")));

        Assert.Equal(oid, user.UserId);
    }

    [Fact]
    public void UserId_NoHttpContext_ThrowsInvalidOperationException()
    {
        var user = UserFor(null);

        Assert.Throws<InvalidOperationException>(() => user.UserId);
    }

    [Fact]
    public void UserId_NoUser_ThrowsInvalidOperationException()
    {
        var user = UserFor(new ClaimsPrincipal());

        Assert.Throws<InvalidOperationException>(() => user.UserId);
    }

    [Fact]
    public void UserId_UnauthenticatedPrincipalWithOid_ThrowsInvalidOperationException()
    {
        var user = UserFor(TestIdentities.Anonymous(TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.Admin)));

        Assert.Throws<InvalidOperationException>(() => user.UserId);
        Assert.Throws<InvalidOperationException>(() => user.IsAdmin);
    }

    [Fact]
    public void UserId_AuthenticatedWithInvalidObjectId_ThrowsInvalidOperationException()
    {
        var user = UserFor(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid()).Replacing("oid", "x")));

        Assert.Throws<InvalidOperationException>(() => user.UserId);
    }

    [Fact]
    public void ThrownMessage_NoUser_IsConstantAndHasNoClaimValues()
    {
        var user = UserFor(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid()).Replacing("oid", "secret-claim-value")));

        var exception = Assert.Throws<InvalidOperationException>(() => user.UserId);

        Assert.Equal("No signed-in user is available.", exception.Message);
    }

    [Fact]
    public void DisplayName_ControlCharacters_AreNormalized()
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.User).With("name", "Al\u0000ice\r\n");

        var user = UserFor(TestIdentities.Authenticated(claims));

        Assert.Equal("Alice", user.DisplayName);
    }

    [Fact]
    public void DisplayName_Missing_IsEmpty()
    {
        var user = UserFor(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.User)));

        Assert.Equal(string.Empty, user.DisplayName);
    }

    [Fact]
    public void DisplayName_Oversized_IsTruncated()
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.User).With("name", new string('n', 10_000));

        var user = UserFor(TestIdentities.Authenticated(claims));

        Assert.Equal(256, user.DisplayName.Length);
    }

    [Theory]
    [InlineData(AppRoles.Admin, true)]
    [InlineData(AppRoles.User, false)]
    public void IsAdmin_Roles_ReflectAdminRoleOnly(string role, bool expected)
    {
        var user = UserFor(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), role)));

        Assert.Equal(expected, user.IsAdmin);
    }
}
