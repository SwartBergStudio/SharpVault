// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.Api.Security;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Api.Security;

/// <summary>
/// TEN-4 and A11: identity is the oid only, and roles come only from the roles claim.
/// </summary>
[Trait("Category", "Security")]
public sealed class SignedInIdentityReaderTests
{
    private static readonly EntraApplicationSettings Settings = TestIdentities.Settings;

    [Fact]
    public void TryRead_ValidPrincipal_ReadsObjectIdNameAndAdminFlag()
    {
        var oid = Guid.NewGuid();
        var claims = TestIdentities.MemberClaims(oid, AppRoles.Admin).With("name", "Alice Example").With("email", "alice@example.test");

        var read = SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.True(read);
        Assert.NotNull(identity);
        Assert.Equal(oid, identity.UserId);
        Assert.Equal("Alice Example", identity.DisplayName);
        Assert.Equal("alice@example.test", identity.Email);
        Assert.Equal(AccountKind.Member, identity.AccountKind);
        Assert.True(identity.IsAdmin);
    }

    [Fact]
    public void TryRead_UserRoleOnly_IsNotAdmin()
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.User);

        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.False(identity!.IsAdmin);
    }

    [Fact]
    public void TryRead_EmailClaim_NeverAffectsUserId()
    {
        var oid = Guid.NewGuid();
        var first = TestIdentities.MemberClaims(oid).With("email", "one@example.test");
        var second = TestIdentities.MemberClaims(oid).With("email", "two@example.test");
        var otherOid = TestIdentities.MemberClaims(Guid.NewGuid()).With("email", "one@example.test");

        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(first), Settings, out var a);
        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(second), Settings, out var b);
        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(otherOid), Settings, out var c);

        Assert.Equal(a!.UserId, b!.UserId);
        Assert.NotEqual(a.UserId, c!.UserId);
    }

    [Fact]
    public void TryRead_EmailMissing_FallsBackToPreferredUsername()
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid()).With("preferred_username", "alice@contoso.test");

        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.Equal("alice@contoso.test", identity!.Email);
    }

    [Fact]
    public void TryRead_NoNameOrEmail_ReturnsNullValues()
    {
        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid())), Settings, out var identity);

        Assert.Null(identity!.DisplayName);
        Assert.Null(identity.Email);
    }

    public static TheoryData<string, string[]> InvalidObjectIds() =>
        new()
        {
            { "missing", [] },
            { "notGuid", ["x"] },
            { "emptyGuid", ["00000000-0000-0000-0000-000000000000"] },
            { "two", ["3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b", "9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a"] },
            { "braces", ["{3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b}"] },
            { "noHyphens", ["3f2a1c9e7b4d4e8a9c215d6e7f809a1b"] },
            { "email", ["alice@example.test"] },
            { "empty", [""] },
        };

    [Theory]
    [MemberData(nameof(InvalidObjectIds))]
    public void TryRead_InvalidObjectId_ReturnsFalse(string caseName, string[] objectIds)
    {
        Assert.NotEmpty(caseName);
        var claims = TestIdentities.MemberClaims(Guid.NewGuid(), AppRoles.User).Without("oid");
        foreach (var value in objectIds)
        {
            claims.Add(new Claim("oid", value));
        }

        var read = SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.False(read);
        Assert.Null(identity);
    }

    [Theory]
    [InlineData("role", "SharpVault.Admin")]
    [InlineData("wids", "SharpVault.Admin")]
    [InlineData("groups", "SharpVault.Admin")]
    [InlineData("scp", "SharpVault.Admin")]
    [InlineData("http://schemas.microsoft.com/ws/2008/06/identity/claims/role", "SharpVault.Admin")]
    [InlineData("roles", "sharpvault.admin")]
    [InlineData("roles", "SHARPVAULT.ADMIN")]
    [InlineData("roles", " SharpVault.Admin")]
    [InlineData("roles", "SharpVault.Admin ")]
    [InlineData("roles", "SharpVault.Admin,SharpVault.User")]
    [InlineData("roles", "SharpVault.Admin SharpVault.User")]
    public void TryRead_RoleClaimOtherThanRoles_IsNotAdmin(string claimType, string value)
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid()).With(claimType, value);

        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.False(identity!.IsAdmin);
    }

    [Fact]
    public void TryRead_AcctMissing_ReportsUnknownAccountKind()
    {
        var claims = TestIdentities.MemberClaims(Guid.NewGuid()).Without("acct");

        SignedInIdentityReader.TryRead(TestIdentities.Authenticated(claims), Settings, out var identity);

        Assert.Equal(AccountKind.Unknown, identity!.AccountKind);
    }

    [Fact]
    public void TryRead_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SignedInIdentityReader.TryRead(null!, Settings, out _));
        Assert.Throws<ArgumentNullException>(() => SignedInIdentityReader.TryRead(new ClaimsPrincipal(), null!, out _));
    }
}
