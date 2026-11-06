// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;

namespace SharpVault.UnitTests.Authentication.Local;

[Trait("Category", "Security")]
public sealed class LocalTokenIssuerTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly FakeTimeProvider _time = new(Start);
    private readonly LocalSigningKey _key = new(RandomNumberGenerator.GetBytes(64));

    private LocalTokenIssuer Issuer => new(_key, _time);

    private static ClaimsPrincipal PrincipalFor(LocalAccount account, params Claim[] extra) =>
        new(new ClaimsIdentity([new Claim("oid", account.Id.ToString("D")), .. extra], "test"));

    private string IssueFor(LocalAccount account) => Issuer.Issue(PrincipalFor(account));

    private TokenValidationParameters Parameters(LocalSigningKey? key = null)
    {
        var parameters = LocalTokenValidation.Create(key ?? _key);
        parameters.LifetimeValidator = (notBefore, expires, _, validation) =>
        {
            var now = _time.GetUtcNow().UtcDateTime;
            return (notBefore is null || notBefore.Value - validation.ClockSkew <= now)
                && (expires is null || expires.Value + validation.ClockSkew >= now);
        };
        return parameters;
    }

    private async Task<TokenValidationResult> ValidateAsync(string token, TokenValidationParameters? parameters = null) =>
        await Handler.ValidateTokenAsync(token, parameters ?? Parameters());

    [Fact]
    public void Issue_KnownAccount_ProducesHs256TokenWithExpectedClaims()
    {
        var account = LocalAccounts.AdminOne;

        var token = new JsonWebToken(IssueFor(account));

        Assert.Equal("HS256", token.Alg);
        Assert.Equal(LocalAuthenticationDefaults.KeyId, token.Kid);
        Assert.Equal(LocalAuthenticationDefaults.Issuer, token.Issuer);
        Assert.Contains(LocalAuthenticationDefaults.Audience, token.Audiences);
        Assert.Equal(account.Id.ToString("D"), token.GetClaim("oid").Value);
        Assert.Equal(LocalAuthenticationDefaults.TenantId, token.GetClaim("tid").Value);
        Assert.Equal(account.DisplayName, token.GetClaim("name").Value);
        Assert.Equal(account.Email, token.GetClaim("preferred_username").Value);
        Assert.Equal(account.Email, token.GetClaim("email").Value);
        Assert.Equal("0", token.GetClaim("acct").Value);
        Assert.Equal("access_as_user", token.GetClaim("scp").Value);
        Assert.Equal(account.Roles.Order(), token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value).Order());
    }

    [Fact]
    public void Issue_Always_SetsFiveMinuteLifetimeAndUniqueJti()
    {
        var first = new JsonWebToken(IssueFor(LocalAccounts.UserOne));
        var second = new JsonWebToken(IssueFor(LocalAccounts.UserOne));

        Assert.Equal(TimeSpan.FromMinutes(5), first.ValidTo - first.ValidFrom);
        Assert.Equal(Start.UtcDateTime, first.ValidFrom);
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Issue_PrincipalWithExtraRoleClaim_IgnoresIt()
    {
        var principal = PrincipalFor(LocalAccounts.UserOne, new Claim("roles", AppRoles.Admin));

        var token = new JsonWebToken(Issuer.Issue(principal));

        Assert.DoesNotContain(token.Claims, claim => claim.Type == "roles" && claim.Value == AppRoles.Admin);
    }

    [Fact]
    public void Issue_UserOnePrincipalWithAdminRoleClaim_TokenHasNoAdminRole()
    {
        var principal = PrincipalFor(LocalAccounts.UserOne, new Claim(ClaimTypes.Role, AppRoles.Admin), new Claim("roles", AppRoles.Admin));

        var token = new JsonWebToken(Issuer.Issue(principal));

        Assert.Equal([AppRoles.User], token.Claims.Where(claim => claim.Type == "roles").Select(claim => claim.Value));
    }

    [Fact]
    public void Issue_UnknownOid_ThrowsWithoutValues()
    {
        var unknown = Guid.NewGuid().ToString("D");
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", unknown)], "test"));

        var error = Assert.Throws<InvalidOperationException>(() => Issuer.Issue(principal));

        Assert.DoesNotContain(unknown, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Issue_TwoOidClaims_Throws()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("oid", LocalAccounts.UserOne.Id.ToString("D")),
                new Claim("oid", LocalAccounts.AdminOne.Id.ToString("D")),
            ],
            "test"));

        Assert.Throws<InvalidOperationException>(() => Issuer.Issue(principal));
    }

    [Fact]
    public void Issue_NoOid_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Issuer.Issue(new ClaimsPrincipal(new ClaimsIdentity("test"))));
    }

    [Fact]
    public async Task Issue_Token_PassesLocalTokenValidation()
    {
        var result = await ValidateAsync(IssueFor(LocalAccounts.AdminTwo));

        Assert.True(result.IsValid, result.Exception?.Message);
    }

    [Fact]
    public async Task Issue_TokenAfterSixMinutes_FailsValidation()
    {
        var token = IssueFor(LocalAccounts.UserOne);
        _time.Advance(TimeSpan.FromMinutes(6));

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Issue_TokenSignedWithOtherKey_FailsValidation()
    {
        var otherKey = new LocalSigningKey(RandomNumberGenerator.GetBytes(64));
        var token = new LocalTokenIssuer(otherKey, _time).Issue(PrincipalFor(LocalAccounts.UserOne));

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Issue_Rs256Token_FailsValidation()
    {
        using var rsa = RSA.Create(2048);
        var descriptor = Descriptor(new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256));

        var result = await ValidateAsync(Handler.CreateToken(descriptor));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Issue_AlgNoneToken_FailsValidation()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(
            "{\"iss\":\"" + LocalAuthenticationDefaults.Issuer + "\",\"aud\":\"" + LocalAuthenticationDefaults.Audience
            + "\",\"exp\":" + Start.AddMinutes(5).ToUnixTimeSeconds() + ",\"oid\":\"" + LocalAccounts.AdminOne.Id + "\"}"));
        var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"typ\":\"JWT\"}"));
        var token = $"{header.TrimEnd('=')}.{payload.TrimEnd('=')}.";

        var result = await ValidateAsync(token);

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("https://other.invalid/issuer", LocalAuthenticationDefaults.Audience)]
    [InlineData(LocalAuthenticationDefaults.Issuer, "api://other")]
    public async Task Issue_WrongIssuer_WrongAudience_FailValidation(string issuer, string audience)
    {
        var descriptor = Descriptor(new SigningCredentials(_key.Key, SecurityAlgorithms.HmacSha256));
        descriptor.Issuer = issuer;
        descriptor.Audience = audience;

        var result = await ValidateAsync(Handler.CreateToken(descriptor));

        Assert.False(result.IsValid);
    }

    [Fact]
    public void LocalTokenValidation_Parameters_AllowOnlyHs256()
    {
        var parameters = LocalTokenValidation.Create(_key);

        Assert.Equal([SecurityAlgorithms.HmacSha256], parameters.ValidAlgorithms);
        Assert.True(parameters.RequireSignedTokens);
        Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.ValidateIssuer);
        Assert.True(parameters.ValidateAudience);
        Assert.Equal(TimeSpan.FromSeconds(30), parameters.ClockSkew);
    }

    private static SecurityTokenDescriptor Descriptor(SigningCredentials credentials) => new()
    {
        Issuer = LocalAuthenticationDefaults.Issuer,
        Audience = LocalAuthenticationDefaults.Audience,
        Expires = Start.UtcDateTime.AddMinutes(5),
        NotBefore = Start.UtcDateTime,
        Subject = new ClaimsIdentity([new Claim("oid", LocalAccounts.AdminOne.Id.ToString("D"))]),
        SigningCredentials = credentials,
    };

    [Fact]
    [Trait("Category", "Performance")]
    public void Issue_ThousandTokens_CompletesUnderOneSecond()
    {
        var principal = PrincipalFor(LocalAccounts.UserOne);
        var issuer = Issuer;
        issuer.Issue(principal);

        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var count = 0; count < 1000; count++)
        {
            issuer.Issue(principal);
        }

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(1), started.Elapsed.ToString());
    }

    [Fact]
    [Trait("Category", "Performance")]
    public async Task Validate_ThousandTokens_CompletesUnderTwoSeconds()
    {
        var token = IssueFor(LocalAccounts.UserOne);
        var parameters = Parameters();
        await Handler.ValidateTokenAsync(token, parameters);

        var started = System.Diagnostics.Stopwatch.StartNew();
        for (var count = 0; count < 1000; count++)
        {
            await Handler.ValidateTokenAsync(token, parameters);
        }

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(2), started.Elapsed.ToString());
    }
}
