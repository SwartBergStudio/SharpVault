// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultLockTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    [Fact]
    public async Task LockedOwner_ValidToken_Returns403OnEveryVaultEndpoint()
    {
        var scenario = await VaultScenario.ArrangeAsync(ApiFor(TestUsers.User));
        var api = ApiFor(TestUsers.User);
        await LockUserAsync(TestUsers.User.ObjectId);
        var before = await RowsOfAsync(TestUsers.User.ObjectId);

        foreach (var endpointId in VaultEndpointCalls.AllIds)
        {
            var response = await VaultEndpointCalls.SendAsync(endpointId, api, scenario);

            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{endpointId} returned {response.StatusCode}.");
            scenario.Markers.AssertAbsentFrom(await BodyOfAsync(response));
        }

        Assert.Equal(before, await RowsOfAsync(TestUsers.User.ObjectId));
    }

    [Fact]
    public async Task LockedOwner_AfterUnlock_SeesVaultAgain()
    {
        var api = ApiFor(TestUsers.User);
        var scenario = await VaultScenario.ArrangeAsync(api);
        await LockUserAsync(TestUsers.User.ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await api.GetSecretAsync(scenario.SecretS)).Response.StatusCode);

        await SetUserStatusAsync(TestUsers.User.ObjectId, UserRows.ActiveStatus);

        var secret = await api.GetSecretAsync(scenario.SecretS);
        Assert.Equal(HttpStatusCode.OK, secret.Response.StatusCode);
        Assert.Equal(scenario.Markers.Name, secret.Body!.Name);
        Assert.Equal(scenario.Markers.Value + "-v2", await RevealValueAsync(api, scenario.SecretS));
    }
}
