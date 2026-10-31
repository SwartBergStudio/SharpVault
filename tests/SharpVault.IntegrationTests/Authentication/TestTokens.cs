// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.IntegrationTests.Authentication;

public static class TestTokens
{
    private const string AccountTypeClaim = "acct";
    private const string IdentityProviderClaim = "idp";
    private const int LifetimeHours = 1;
    private const int NotBeforeSkewMinutes = 1;

    public static string Create(TestUser user) => For(user).Build();

    public static TestTokenBuilder For(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["iss"] = TestIdentityProvider.Issuer(user.TenantId),
            ["aud"] = TestApi.ClientId.ToString("D"),
            ["tid"] = user.TenantId.ToString("D"),
            ["oid"] = user.ObjectId.ToString("D"),
            ["name"] = user.DisplayName,
            ["preferred_username"] = user.Email ?? user.ObjectId.ToString("D"),
            ["scp"] = TestApi.Scope,
            ["ver"] = "2.0",
            [AccountTypeClaim] = user.IsGuest ? 1 : 0,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.AddMinutes(-NotBeforeSkewMinutes).ToUnixTimeSeconds(),
            ["exp"] = now.AddHours(LifetimeHours).ToUnixTimeSeconds(),
        };

        if (user.Email is not null)
        {
            claims["email"] = user.Email;
        }

        if (user.Roles.Count > 0)
        {
            claims["roles"] = user.Roles.ToArray();
        }

        if (user.IsGuest)
        {
            claims[IdentityProviderClaim] = $"https://sts.windows.net/{TestTenant.OtherId:D}/";
        }

        return new TestTokenBuilder(claims);
    }

    public static string ForObjectId(Guid objectId)
    {
        var user = TestUsers.All.FirstOrDefault(candidate => candidate.ObjectId == objectId)
            ?? throw new InvalidOperationException("No test user has the requested object id.");
        return Create(user);
    }
}
