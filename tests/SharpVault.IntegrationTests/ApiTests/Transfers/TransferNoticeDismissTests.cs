// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferNoticeDismissTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser];

    [Fact]
    public async Task Dismiss_RemovesTheNotice()
    {
        await WarmUpAllAsync(AllParticipants);
        await SeedVaultAsync(TestUsers.User, secretCount: 1, folderCount: 0);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);
        await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);
        var before = Require(await ApiAs(TestUsers.OtherUser).SendAsync<TransferNoticeListDto>(HttpMethod.Get, ApiRoutes.TransferNotices));
        Assert.Contains(before.Items, notice => notice.TransferId == transfer.Id);
        var path = VaultTestApi.Bind(ApiRoutes.TransferNotice, ("transferId", transfer.Id));

        var dismissed = await ApiAs(TestUsers.OtherUser).Client.DeleteAsync(path, Token);

        Assert.Equal(HttpStatusCode.NoContent, dismissed.StatusCode);
        var after = Require(await ApiAs(TestUsers.OtherUser).SendAsync<TransferNoticeListDto>(HttpMethod.Get, ApiRoutes.TransferNotices));
        Assert.DoesNotContain(after.Items, notice => notice.TransferId == transfer.Id);
        Assert.Equal(before.Items.Count - 1, after.Items.Count);
        var repeat = await ApiAs(TestUsers.OtherUser).Client.DeleteAsync(path, Token);
        Assert.Equal(HttpStatusCode.NotFound, repeat.StatusCode);
    }
}
