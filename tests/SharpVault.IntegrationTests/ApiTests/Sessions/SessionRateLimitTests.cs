// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Sessions;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SessionRateLimitTests(ApiTestFactory factory) : ApiSecurityTestBase(factory)
{
    private const int VaultRequestsPerMinute = 3;
    private const int ValidSessionHours = 1;

    [Fact]
    public async Task SessionRevoke_OverTheVaultLimit_Returns429()
    {
        await using var host = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("VaultRateLimits:RequestsPerMinute", VaultRequestsPerMinute.ToString(CultureInfo.InvariantCulture)));
        using var client = CreateClientFor(host, TestTokens.Create(TestUsers.User));

        for (var call = 0; call < VaultRequestsPerMinute; call++)
        {
            var allowed = await client.PostAsJsonAsync(ApiRoutes.MeSessionRevoke, ValidBody(), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await client.PostAsJsonAsync(ApiRoutes.MeSessionRevoke, ValidBody(), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
    }

    private static SessionReferenceRequestDto ValidBody() => new()
    {
        SessionId = Guid.NewGuid().ToString("N"),
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(ValidSessionHours),
    };
}
