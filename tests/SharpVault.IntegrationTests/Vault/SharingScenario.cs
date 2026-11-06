// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;

namespace SharpVault.IntegrationTests.Vault;

public sealed record SharingScenario(
    TestUser Owner,
    PlaintextMarkers SecretMarkers,
    PlaintextMarkers NestedMarkers,
    Guid FolderF,
    Guid FolderG,
    Guid SecretS,
    Guid SecretT,
    string SecretToken,
    string NestedToken,
    string ViewerGrantToken,
    string ManagerGranteeGrantToken,
    string FolderViewerGrantToken)
{
    public Guid OwnerId => Owner.ObjectId;

    public IReadOnlyList<Guid> Ids => [FolderF, FolderG, SecretS, SecretT];

    public IReadOnlyList<PlaintextMarkers> Markers => [SecretMarkers, NestedMarkers];

    public static async Task<SharingScenario> ArrangeAsync(Func<TestUser, HttpClient> clientFor)
    {
        ArgumentNullException.ThrowIfNull(clientFor);

        var owner = TestUsers.User;
        using var ownerClient = clientFor(owner);
        var ownerApi = new VaultTestApi(ownerClient);
        var ownerSharing = new SharingTestApi(ownerClient);

        var secretMarkers = new PlaintextMarkers();
        var nestedMarkers = new PlaintextMarkers();
        var folderF = Require(await ownerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = secretMarkers.Folder + "-F" }));
        var folderG = Require(await ownerApi.CreateFolderAsync(new CreateFolderRequestDto { Name = secretMarkers.Folder + "-G", ParentId = folderF.Id }));
        var secretS = Require(await ownerApi.CreateSecretAsync(NewSecret(secretMarkers, null)));
        var secretT = Require(await ownerApi.CreateSecretAsync(NewSecret(nestedMarkers, folderG.Id)));

        var viewer = await GrantSecretAsync(ownerSharing, owner, secretS.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await GrantSecretAsync(ownerSharing, owner, secretS.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await GrantSecretAsync(ownerSharing, owner, secretS.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        await GrantSecretAsync(ownerSharing, owner, secretS.Id, TestUsers.LockedRecipient, SharePermissionDto.Manage);

        using var managerClient = clientFor(TestUsers.ShareManager);
        var granteeGrant = Require(await new SharingTestApi(managerClient).CreateSecretGrantAsync(
            owner.ObjectId,
            secretS.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.ManagerGrantee.ObjectId, Permission = SharePermissionDto.View }));

        var folderViewer = Require(await ownerSharing.CreateFolderGrantAsync(
            owner.ObjectId,
            folderF.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.FolderViewer.ObjectId, Permission = SharePermissionDto.View }));
        Require(await ownerSharing.CreateFolderGrantAsync(
            owner.ObjectId,
            folderF.Id,
            new CreateShareGrantRequestDto { RecipientId = TestUsers.FolderManager.ObjectId, Permission = SharePermissionDto.Manage }));

        var current = Require(await ownerApi.GetSecretAsync(secretS.Id));
        var nested = Require(await ownerApi.GetSecretAsync(secretT.Id));
        return new SharingScenario(
            owner,
            secretMarkers,
            nestedMarkers,
            folderF.Id,
            folderG.Id,
            secretS.Id,
            secretT.Id,
            current.ConcurrencyToken,
            nested.ConcurrencyToken,
            viewer.ConcurrencyToken,
            granteeGrant.ConcurrencyToken,
            folderViewer.ConcurrencyToken);
    }

    public static CreateSecretRequestDto NewSecret(PlaintextMarkers markers, Guid? folderId) => new()
    {
        Type = SecretTypeDto.Login,
        Name = markers.Name,
        Username = markers.Username,
        Url = markers.Url,
        Tags = [markers.Tag],
        Value = markers.Value,
        Notes = markers.Notes,
        FolderId = folderId,
    };

    private static async Task<ShareGrantDto> GrantSecretAsync(SharingTestApi api, TestUser owner, Guid secretId, TestUser recipient, SharePermissionDto permission) =>
        Require(await api.CreateSecretGrantAsync(
            owner.ObjectId,
            secretId,
            new CreateShareGrantRequestDto { RecipientId = recipient.ObjectId, Permission = permission }));

    private static T Require<T>((HttpResponseMessage Response, T? Body) result)
        where T : class
    {
        Assert.True(result.Response.IsSuccessStatusCode, $"Arrangement failed with {result.Response.StatusCode}.");
        Assert.NotEqual(HttpStatusCode.NoContent, result.Response.StatusCode);
        return result.Body ?? throw new InvalidOperationException("The arrangement response had no body.");
    }
}
