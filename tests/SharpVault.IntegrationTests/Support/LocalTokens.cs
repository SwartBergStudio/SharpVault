// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Support;

public static class LocalTokens
{
    public static string For(IServiceProvider services, LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(account);

        var issuer = services.GetRequiredService<ILocalTokenIssuer>();
        return issuer.Issue(LocalPrincipalFactory.Create(account, "LocalTokens"));
    }

    public static string AlgNone(LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var throwawayKey = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(LocalAuthenticationDefaults.KeyLength));
        var payload = Builder(account, throwawayKey).Build().Split('.')[1];
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"none","typ":"JWT"}"""));
        return $"{header}.{payload}.";
    }

    public static TestTokenBuilder Builder(IServiceProvider services, LocalAccount account)
    {
        ArgumentNullException.ThrowIfNull(services);

        return Builder(account, services.GetRequiredService<LocalSigningKey>().Key);
    }

    public static TestTokenBuilder Builder(LocalAccount account, SecurityKey key)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(key);

        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = LocalAuthenticationDefaults.Issuer,
            ["aud"] = LocalAuthenticationDefaults.Audience,
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            [EntraClaimTypes.ObjectId] = account.Id.ToString("D"),
            [EntraClaimTypes.TenantId] = LocalAuthenticationDefaults.TenantId,
            [EntraClaimTypes.Name] = account.DisplayName,
            [EntraClaimTypes.Scope] = LocalAuthenticationDefaults.AccessScope,
            [EntraClaimTypes.AccountType] = EntraClaimTypes.MemberAccountType,
            [EntraClaimTypes.Roles] = account.Roles.ToArray(),
        };
        return new TestTokenBuilder(claims).SignedWith(key, LocalAuthenticationDefaults.SigningAlgorithm);
    }
}
