// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using SharpVault.Dtos;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferAdminRateLimitTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const int AdminActionsPerMinute = 3;

    [Fact]
    public async Task AdminActions_OverLimit_Returns429WithRetryAfter()
    {
        UseHost(Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("VaultRateLimits:AdminActionsPerMinute", AdminActionsPerMinute.ToString(CultureInfo.InvariantCulture))));
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin);
        var admin = ApiAs(TestUsers.Admin).Client;

        for (var call = 0; call < AdminActionsPerMinute; call++)
        {
            var allowed = await admin.GetAsync(ApiRoutes.AdminTransfers, Token);
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        }

        var limited = await admin.GetAsync(ApiRoutes.AdminTransfers, Token);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        var otherAdmin = await ApiAs(TestUsers.SecondAdmin).Client.GetAsync(ApiRoutes.AdminTransfers, Token);
        Assert.Equal(HttpStatusCode.OK, otherAdmin.StatusCode);
    }
}
