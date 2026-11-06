// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultGroupLimitTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const int GroupLimit = 5;

    [Fact]
    public async Task VaultRequests_OverGroupLimit_Return429()
    {
        await using var host = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("VaultRateLimits:RequestsPerMinute", GroupLimit.ToString(CultureInfo.InvariantCulture));
            builder.UseSetting("VaultRateLimits:RevealsPerMinute", "100");
        });
        var api = ApiOn(host, TestUsers.User);
        var markers = new PlaintextMarkers();

        Assert.Equal(HttpStatusCode.OK, (await api.ListSecretsAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ListFoldersAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ListRecycleBinAsync()).Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await api.CreateSecretAsync(NewSecretRequest(markers))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ListSecretsAsync()).Response.StatusCode);
        var before = await RowsOfAsync(TestUsers.User.ObjectId);
        ResetCounters();

        var limited = await api.CreateSecretAsync(NewSecretRequest(new PlaintextMarkers()));

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Response.StatusCode);
        Assert.NotNull(limited.Response.Headers.RetryAfter);
        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Equal(before, await RowsOfAsync(TestUsers.User.ObjectId));
    }
}
