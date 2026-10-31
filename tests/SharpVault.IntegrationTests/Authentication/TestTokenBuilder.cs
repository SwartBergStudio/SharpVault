// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SharpVault.IntegrationTests.Authentication;

public sealed class TestTokenBuilder
{
    private readonly Dictionary<string, object> _claims;
    private SecurityKey _key = TestSigningKeys.Primary;
    private string _algorithm = SecurityAlgorithms.RsaSha256;

    public TestTokenBuilder(IDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        _claims = new Dictionary<string, object>(claims, StringComparer.Ordinal);
    }

    public TestTokenBuilder WithClaim(string type, object value)
    {
        _claims[type] = value;
        return this;
    }

    public TestTokenBuilder WithoutClaim(string type)
    {
        _claims.Remove(type);
        return this;
    }

    public TestTokenBuilder ExpiresAt(DateTimeOffset expiresAt) => WithClaim("exp", expiresAt.ToUnixTimeSeconds());

    public TestTokenBuilder NotBefore(DateTimeOffset notBefore) => WithClaim("nbf", notBefore.ToUnixTimeSeconds());

    public TestTokenBuilder SignedWith(SecurityKey key, string algorithm)
    {
        ArgumentNullException.ThrowIfNull(key);

        _key = key;
        _algorithm = algorithm;
        return this;
    }

    public string Build()
    {
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = _claims,
            SigningCredentials = new SigningCredentials(_key, _algorithm),
        };
        return handler.CreateToken(descriptor);
    }
}
