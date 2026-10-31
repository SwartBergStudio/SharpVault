// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.ServiceDefaults.Identity;

/// <summary>
/// TEN-2: the tenant claim is checked explicitly because the issuer validator does not compare it.
/// </summary>
[Trait("Category", "Security")]
public sealed class EntraTokenChecksTests
{
    private const string Scope = "access_as_user";

    private static readonly EntraApplicationSettings Settings = TestIdentities.Settings;

    private static TokenCheckResult Access(List<Claim> claims) =>
        EntraTokenChecks.CheckAccessToken(TestIdentities.Authenticated(claims), Settings, Scope);

    [Fact]
    public void CheckAccessToken_ValidClaims_ReturnsValid()
    {
        Assert.Equal(TokenCheckResult.Valid, Access(TestIdentities.AccessTokenClaims()));
    }

    [Fact]
    public void CheckAccessToken_UppercaseGuidClaims_ReturnsValid()
    {
        var claims = TestIdentities.AccessTokenClaims()
            .Replacing(EntraClaimTypes.TenantId, TestIdentities.TenantId.ToString("D").ToUpperInvariant());

        Assert.Equal(TokenCheckResult.Valid, Access(claims));
    }

    public static TheoryData<string, string[]> TenantCases() =>
        new()
        {
            { "foreign", [TestIdentities.OtherTenantId.ToString("D")] },
            { "missing", [] },
            { "duplicated", [TestIdentities.TenantId.ToString("D"), TestIdentities.TenantId.ToString("D")] },
            { "duplicatedWithForeign", [TestIdentities.TenantId.ToString("D"), TestIdentities.OtherTenantId.ToString("D")] },
            { "foreignFirst", [TestIdentities.OtherTenantId.ToString("D"), TestIdentities.TenantId.ToString("D")] },
            { "common", ["common"] },
            { "organizations", ["organizations"] },
            { "empty", [string.Empty] },
            { "nonGuid", ["not-a-guid"] },
            { "braces", ["{" + TestIdentities.TenantId.ToString("D") + "}"] },
            { "noHyphens", [TestIdentities.TenantId.ToString("N")] },
            { "emptyGuid", [Guid.Empty.ToString("D")] },
        };

    [Theory]
    [MemberData(nameof(TenantCases))]
    public void CheckAccessToken_TenantMismatchMissingOrDuplicated_ReturnsWrongTenant(string caseName, string[] tenantValues)
    {
        Assert.NotEmpty(caseName);
        var claims = TestIdentities.AccessTokenClaims().Without(EntraClaimTypes.TenantId);
        foreach (var value in tenantValues)
        {
            claims.Add(new Claim(EntraClaimTypes.TenantId, value));
        }

        Assert.Equal(TokenCheckResult.WrongTenant, Access(claims));
    }

    public static TheoryData<string, string[]> ObjectIdCases() =>
        new()
        {
            { "missing", [] },
            { "notGuid", ["x"] },
            { "emptyGuid", [Guid.Empty.ToString("D")] },
            { "two", [Guid.NewGuid().ToString("D"), Guid.NewGuid().ToString("D")] },
            { "twoIdentical", [TestIdentities.ClientId.ToString("D"), TestIdentities.ClientId.ToString("D")] },
            { "braces", ["{" + Guid.NewGuid().ToString("D") + "}"] },
            { "noHyphens", [Guid.NewGuid().ToString("N")] },
            { "empty", [string.Empty] },
            { "email", ["alice@example.test"] },
            { "upn", ["alice@contoso.onmicrosoft.com"] },
        };

    [Theory]
    [MemberData(nameof(ObjectIdCases))]
    public void CheckAccessToken_InvalidObjectId_ReturnsInvalidObjectId(string caseName, string[] objectIds)
    {
        Assert.NotEmpty(caseName);
        var claims = TestIdentities.AccessTokenClaims().Without(EntraClaimTypes.ObjectId);
        foreach (var value in objectIds)
        {
            claims.Add(new Claim(EntraClaimTypes.ObjectId, value));
        }

        Assert.Equal(TokenCheckResult.InvalidObjectId, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_WrongTenantAndInvalidObjectId_ReportsWrongTenantFirst()
    {
        var claims = TestIdentities.AccessTokenClaims()
            .Replacing(EntraClaimTypes.TenantId, TestIdentities.OtherTenantId.ToString("D"))
            .Replacing(EntraClaimTypes.ObjectId, "x");

        Assert.Equal(TokenCheckResult.WrongTenant, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_AppIdentityType_ReturnsAppOnlyToken()
    {
        var claims = TestIdentities.AccessTokenClaims().With(EntraClaimTypes.IdentityType, "app");

        Assert.Equal(TokenCheckResult.AppOnlyToken, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_AppIdentityTypeAmongOthers_ReturnsAppOnlyToken()
    {
        var claims = TestIdentities.AccessTokenClaims()
            .With(EntraClaimTypes.IdentityType, "user")
            .With(EntraClaimTypes.IdentityType, "app");

        Assert.Equal(TokenCheckResult.AppOnlyToken, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_AppOnlyTokenWithoutScope_ReturnsAppOnlyToken()
    {
        var claims = TestIdentities.AccessTokenClaims()
            .Without(EntraClaimTypes.Scope)
            .With(EntraClaimTypes.IdentityType, "app")
            .With(EntraClaimTypes.Roles, AppRoles.Admin);

        Assert.Equal(TokenCheckResult.AppOnlyToken, Access(claims));
    }

    [Theory]
    [InlineData("User")]
    [InlineData("")]
    [InlineData("APP")]
    public void CheckAccessToken_OtherIdentityTypes_AreNotTreatedAsApp(string identityType)
    {
        var claims = TestIdentities.AccessTokenClaims().With(EntraClaimTypes.IdentityType, identityType);

        Assert.Equal(TokenCheckResult.Valid, Access(claims));
    }

    public static TheoryData<string, string[]> ScopeCases() =>
        new()
        {
            { "none", [] },
            { "other", ["User.Read"] },
            { "prefixOnly", ["access_as_user_admin"] },
            { "suffixed", ["xaccess_as_user"] },
            { "upperCase", ["ACCESS_AS_USER"] },
            { "empty", [string.Empty] },
            { "whitespace", ["   "] },
            { "twoClaims", ["access_as_user", "access_as_user"] },
            { "twoClaimsOtherFirst", ["User.Read", "access_as_user"] },
            { "tabSeparated", ["User.Read\taccess_as_user"] },
            { "commaSeparated", ["User.Read,access_as_user"] },
            { "fullApiScope", ["api://a1b2c3d4-e5f6-4708-8192-a3b4c5d6e7f8/access_as_user"] },
        };

    [Theory]
    [MemberData(nameof(ScopeCases))]
    public void CheckAccessToken_ScopeMissingOtherOrPrefixOnly_ReturnsMissingScope(string caseName, string[] scopes)
    {
        Assert.NotEmpty(caseName);
        var claims = TestIdentities.AccessTokenClaims().Without(EntraClaimTypes.Scope);
        foreach (var value in scopes)
        {
            claims.Add(new Claim(EntraClaimTypes.Scope, value));
        }

        Assert.Equal(TokenCheckResult.MissingScope, Access(claims));
    }

    [Theory]
    [InlineData("User.Read access_as_user")]
    [InlineData("access_as_user User.Read")]
    [InlineData("User.Read   access_as_user  profile")]
    [InlineData(" access_as_user ")]
    public void CheckAccessToken_ScopeAmongOthers_ReturnsValid(string scopes)
    {
        var claims = TestIdentities.AccessTokenClaims().Replacing(EntraClaimTypes.Scope, scopes);

        Assert.Equal(TokenCheckResult.Valid, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_RolesClaimWithoutScope_ReturnsMissingScope()
    {
        var claims = TestIdentities.AccessTokenClaims().Without(EntraClaimTypes.Scope).With(EntraClaimTypes.Roles, AppRoles.Admin);

        Assert.Equal(TokenCheckResult.MissingScope, Access(claims));
    }

    [Fact]
    public void CheckAccessToken_NullPrincipal_ReturnsInvalid()
    {
        Assert.Equal(TokenCheckResult.Invalid, EntraTokenChecks.CheckAccessToken(null, Settings, Scope));
    }

    [Fact]
    public void CheckAccessToken_EmptyPrincipal_ReturnsWrongTenant()
    {
        Assert.Equal(TokenCheckResult.WrongTenant, EntraTokenChecks.CheckAccessToken(new ClaimsPrincipal(), Settings, Scope));
    }

    [Fact]
    public void CheckAccessToken_NullSettings_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EntraTokenChecks.CheckAccessToken(new ClaimsPrincipal(), null!, Scope));
    }

    [Fact]
    public void CheckAccessToken_RequiredScopeDiffersFromDefault_UsesGivenScope()
    {
        var claims = TestIdentities.AccessTokenClaims();

        var result = EntraTokenChecks.CheckAccessToken(TestIdentities.Authenticated(claims), Settings, "other_scope");

        Assert.Equal(TokenCheckResult.MissingScope, result);
    }

    [Fact]
    public void CheckIdToken_OtherTenant_ReturnsWrongTenant()
    {
        var claims = TestIdentities.AccessTokenClaims().Replacing(EntraClaimTypes.TenantId, TestIdentities.OtherTenantId.ToString("D"));

        Assert.Equal(TokenCheckResult.WrongTenant, EntraTokenChecks.CheckIdToken(TestIdentities.Authenticated(claims), Settings));
    }

    [Fact]
    public void CheckIdToken_NoScope_ReturnsValid()
    {
        var claims = TestIdentities.AccessTokenClaims().Without(EntraClaimTypes.Scope);

        Assert.Equal(TokenCheckResult.Valid, EntraTokenChecks.CheckIdToken(TestIdentities.Authenticated(claims), Settings));
    }

    [Fact]
    public void CheckIdToken_InvalidObjectId_ReturnsInvalidObjectId()
    {
        var claims = TestIdentities.AccessTokenClaims().Replacing(EntraClaimTypes.ObjectId, Guid.Empty.ToString("D"));

        Assert.Equal(TokenCheckResult.InvalidObjectId, EntraTokenChecks.CheckIdToken(TestIdentities.Authenticated(claims), Settings));
    }

    [Fact]
    public void CheckIdToken_NullPrincipal_ReturnsInvalid()
    {
        Assert.Equal(TokenCheckResult.Invalid, EntraTokenChecks.CheckIdToken(null, Settings));
    }

    [Fact]
    public void TokenCheckResult_Default_IsNotValid()
    {
        Assert.Equal(TokenCheckResult.Invalid, default(TokenCheckResult));
        Assert.NotEqual(TokenCheckResult.Valid, default(TokenCheckResult));
    }
}
