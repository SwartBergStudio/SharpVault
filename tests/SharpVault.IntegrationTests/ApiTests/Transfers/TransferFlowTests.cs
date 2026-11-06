// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Users;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
public sealed class TransferFlowTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    [Fact]
    public async Task Transfer_FullFlow_TargetGetsEverythingAndSourceIsEmpty()
    {
        await WarmUpAllAsync(TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser);
        var seeded = await SeedVaultAsync(TestUsers.User, secretCount: 3, folderCount: 2);

        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        var targetSecrets = await ListAllSecretsAsync(TestUsers.OtherUser);
        Assert.Equal(seeded.Secrets.Count, targetSecrets.Count);
        Assert.Empty(await ListAllSecretsAsync(TestUsers.User));
    }
}
