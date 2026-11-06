// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Users;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class UserCountsRateLimitTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const int AdminActionsPerMinute = 3;

    [Fact]
    public async Task GetUserCounts_OverTheAdminLimit_Returns429()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.SecondAdmin);
        await WarmUpAsync(TestUsers.User);
        await using var host = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("VaultRateLimits:AdminActionsPerMinute", AdminActionsPerMinute.ToString(CultureInfo.InvariantCulture)));
        using var admin = CreateClientFor(host, TestTokens.Create(TestUsers.Admin));
        var path = CountsPath(TestUsers.User.ObjectId);

        for (var call = 0; call < AdminActionsPerMinute; call++)
        {
            var allowed = await admin.GetAsync(path, Token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await admin.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        using var otherAdmin = CreateClientFor(host, TestTokens.Create(TestUsers.SecondAdmin));
        var otherResponse = await otherAdmin.GetAsync(path, Token);
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        Assert.NotNull(await otherResponse.Content.ReadFromJsonAsync<UserCountsDto>(Token));
    }

    private static string CountsPath(Guid userId) => ApiRoutes.AdminUserCounts.Replace("{userId:guid}", userId.ToString("D"), StringComparison.Ordinal);
}
