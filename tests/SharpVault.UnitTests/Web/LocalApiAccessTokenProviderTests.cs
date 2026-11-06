// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class LocalApiAccessTokenProviderTests
{
    private readonly LocalApiAccessTokenProvider _provider = new(
        new LocalTokenIssuer(new LocalSigningKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength)), TimeProvider.System),
        NullLogger<LocalApiAccessTokenProvider>.Instance);

    [Fact]
    public async Task Get_KnownAccount_ReturnsBearerTokenForThatOid()
    {
        var principal = LocalPrincipalFactory.Create(LocalAccounts.AdminOne, "Test");

        var token = await _provider.GetAccessTokenAsync(principal, TestContext.Current.CancellationToken);

        Assert.Equal(ApiAccessTokenKind.Bearer, token.Kind);
        var parsed = new JsonWebToken(token.Value);
        Assert.Equal(LocalAccounts.AdminOne.Id.ToString("D"), parsed.GetClaim(EntraClaimTypes.ObjectId).Value);
    }

    [Fact]
    public async Task Get_TwoPrincipals_GetTokensForTheirOwnOid()
    {
        var first = await _provider.GetAccessTokenAsync(LocalPrincipalFactory.Create(LocalAccounts.UserOne, "Test"), TestContext.Current.CancellationToken);
        var second = await _provider.GetAccessTokenAsync(LocalPrincipalFactory.Create(LocalAccounts.UserTwo, "Test"), TestContext.Current.CancellationToken);

        Assert.Equal(LocalAccounts.UserOne.Id.ToString("D"), new JsonWebToken(first.Value).GetClaim(EntraClaimTypes.ObjectId).Value);
        Assert.Equal(LocalAccounts.UserTwo.Id.ToString("D"), new JsonWebToken(second.Value).GetClaim(EntraClaimTypes.ObjectId).Value);
        Assert.NotEqual(first.Value, second.Value);
    }

    [Fact]
    public async Task Get_UnknownAccount_ReturnsSignInRequired()
    {
        var identity = new ClaimsIdentity([new Claim(EntraClaimTypes.ObjectId, Guid.NewGuid().ToString("D"))], "Test");

        var token = await _provider.GetAccessTokenAsync(new ClaimsPrincipal(identity), TestContext.Current.CancellationToken);

        Assert.Equal(ApiAccessTokenKind.SignInRequired, token.Kind);
        Assert.Null(token.Value);
    }

    [Fact]
    public async Task Get_PrincipalClaimingAdminRole_StillGetsCatalogueRoles()
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(EntraClaimTypes.ObjectId, LocalAccounts.UserOne.Id.ToString("D")),
                new Claim(EntraClaimTypes.Roles, AppRoles.Admin),
            ],
            "Test");

        var token = await _provider.GetAccessTokenAsync(new ClaimsPrincipal(identity), TestContext.Current.CancellationToken);

        var roles = new JsonWebToken(token.Value).Claims.Where(claim => claim.Type == EntraClaimTypes.Roles).Select(claim => claim.Value);
        Assert.Equal([AppRoles.User], roles);
    }

    [Fact]
    public void InteractiveSignInAvailable_Always_IsFalse()
    {
        Assert.False(_provider.InteractiveSignInAvailable);
    }
}
