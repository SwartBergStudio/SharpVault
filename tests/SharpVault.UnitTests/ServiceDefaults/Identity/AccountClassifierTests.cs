// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.ServiceDefaults.Identity;

/// <summary>
/// TEN-3: guests are detected from the acct claim with an idp cross-check, and anything unclear fails closed.
/// </summary>
[Trait("Category", "Security")]
public sealed class AccountClassifierTests
{
    private static readonly EntraApplicationSettings Settings = TestIdentities.Settings;

    private static AccountKind Classify(params Claim[] claims) =>
        AccountClassifier.Classify(TestIdentities.Authenticated(claims), Settings);

    [Fact]
    public void Classify_AcctZeroWithoutIdp_ReturnsMember()
    {
        Assert.Equal(AccountKind.Member, Classify(new Claim("acct", "0")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Classify_AcctZeroWithOwnTenantIdp_ReturnsMember(bool v2)
    {
        var idp = v2 ? Settings.V2Issuer : Settings.V1Issuer;

        Assert.Equal(AccountKind.Member, Classify(new Claim("acct", "0"), new Claim("idp", idp)));
    }

    [Fact]
    public void Classify_AcctOne_ReturnsGuest()
    {
        Assert.Equal(AccountKind.Guest, Classify(new Claim("acct", "1")));
    }

    [Fact]
    public void Classify_AcctOneWithOwnTenantIdp_ReturnsGuest()
    {
        Assert.Equal(AccountKind.Guest, Classify(new Claim("acct", "1"), new Claim("idp", Settings.V2Issuer)));
    }

    [Theory]
    [InlineData("https://sts.windows.net/9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a/")]
    [InlineData("https://login.microsoftonline.com/9d8c7b6a-5f4e-4d3c-8b2a-1f0e9d8c7b6a/v2.0")]
    [InlineData("live.com")]
    [InlineData("mail")]
    [InlineData("google.com")]
    [InlineData("")]
    [InlineData("HTTPS://STS.WINDOWS.NET/3F2A1C9E-7B4D-4E8A-9C21-5D6E7F809A1B/")]
    [InlineData("https://sts.windows.net/3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b")]
    public void Classify_AcctZeroWithForeignIdp_ReturnsGuest(string idp)
    {
        Assert.Equal(AccountKind.Guest, Classify(new Claim("acct", "0"), new Claim("idp", idp)));
    }

    [Fact]
    public void Classify_AcctZeroWithOwnAndForeignIdp_ReturnsGuest()
    {
        var kind = Classify(new Claim("acct", "0"), new Claim("idp", Settings.V2Issuer), new Claim("idp", "live.com"));

        Assert.Equal(AccountKind.Guest, kind);
    }

    [Fact]
    public void Classify_AcctZeroWithForeignIdpFirstThenOwn_ReturnsGuest()
    {
        var kind = Classify(new Claim("acct", "0"), new Claim("idp", "live.com"), new Claim("idp", Settings.V2Issuer));

        Assert.Equal(AccountKind.Guest, kind);
    }

    public static TheoryData<string, string[]> UnexpectedAcctCases() =>
        new()
        {
            { "missing", [] },
            { "two", ["2"] },
            { "empty", [string.Empty] },
            { "twoClaims", ["0", "0"] },
            { "memberThenGuest", ["0", "1"] },
            { "guestThenMember", ["1", "0"] },
            { "spaced", [" 0"] },
            { "text", ["member"] },
            { "negative", ["-1"] },
            { "doubleZero", ["00"] },
        };

    [Theory]
    [MemberData(nameof(UnexpectedAcctCases))]
    public void Classify_AcctMissingOrUnexpected_ReturnsUnknown(string caseName, string[] acctValues)
    {
        Assert.NotEmpty(caseName);
        var claims = acctValues.Select(value => new Claim("acct", value)).ToArray();

        Assert.Equal(AccountKind.Unknown, Classify(claims));
    }

    [Fact]
    public void Classify_MissingAcctButOwnTenantIdp_ReturnsUnknown()
    {
        Assert.Equal(AccountKind.Unknown, Classify(new Claim("idp", Settings.V2Issuer)));
    }

    [Fact]
    public void Classify_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => AccountClassifier.Classify(null!, Settings));
        Assert.Throws<ArgumentNullException>(() => AccountClassifier.Classify(new ClaimsPrincipal(), null!));
    }

    [Fact]
    public void AccountKind_Default_IsUnknown()
    {
        Assert.Equal(AccountKind.Unknown, default(AccountKind));
    }
}
