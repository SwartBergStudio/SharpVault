// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Transfers;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class TransferFolderShareTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string FolderValue = "value-inside-the-shared-folder";

    private static TestUser[] AllParticipants => [TestUsers.Admin, TestUsers.SecondAdmin, TestUsers.User, TestUsers.OtherUser, TestUsers.ShareViewer];

    [Fact]
    public async Task Transfer_FolderShare_RecipientReadsThroughTheNewOwner()
    {
        await WarmUpAllAsync(AllParticipants);
        var owner = ApiAs(TestUsers.User);
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "Shared folder" }));
        var secret = Require(await owner.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "Inside", Value = FolderValue, FolderId = folder.Id }));
        var sharing = new SharingTestApi(owner.Client);
        var (grant, _) = await sharing.CreateFolderGrantAsync(
            TestUsers.User.ObjectId,
            folder.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.ShareViewer.ObjectId, Permission = SharePermissionDto.View });
        Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
        var viewer = new SharingTestApi(ApiAs(TestUsers.ShareViewer).Client);
        var (beforeTransfer, _) = await viewer.GetSharedSecretAsync(TestUsers.User.ObjectId, secret.Id);
        Assert.Equal(HttpStatusCode.OK, beforeTransfer.StatusCode);
        var transfer = await RequestAsync(TestUsers.Admin, TestUsers.User, TestUsers.OtherUser);
        await ApproveAsync(TestUsers.SecondAdmin, transfer);

        var settled = await RunUntilSettledAsync(TestUsers.Admin, transfer.Id, TransferStateDto.Completed);

        Assert.Equal(TransferStateDto.Completed, settled.State);
        var (oldOwnerRead, _) = await viewer.GetSharedSecretAsync(TestUsers.User.ObjectId, secret.Id);
        Assert.Equal(HttpStatusCode.NotFound, oldOwnerRead.StatusCode);
        var (newOwnerRead, _) = await viewer.GetSharedSecretAsync(TestUsers.OtherUser.ObjectId, secret.Id);
        Assert.Equal(HttpStatusCode.OK, newOwnerRead.StatusCode);
        var (reveal, revealed) = await viewer.RevealSharedSecretAsync(
            TestUsers.OtherUser.ObjectId,
            secret.Id,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View });
        Assert.Equal(HttpStatusCode.OK, reveal.StatusCode);
        Assert.Equal(FolderValue, revealed?.Value);
    }
}
