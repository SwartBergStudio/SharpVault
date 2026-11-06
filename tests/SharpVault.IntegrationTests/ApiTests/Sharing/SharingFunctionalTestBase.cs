// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.ApiTests.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Storage;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

public abstract class SharingFunctionalTestBase : VaultTestBase
{
    protected SharingFunctionalTestBase(ApiTestFactory factory)
        : base(factory)
    {
        OwnerVault = ApiFor(Owner);
        OwnerSharing = new SharingTestApi(OwnerVault.Client);
    }

    protected static TestUser Owner => TestUsers.User;

    protected VaultTestApi OwnerVault { get; }

    protected SharingTestApi OwnerSharing { get; }

    protected SharingTestApi SharingFor(TestUser user) => new(ClientFor(user));

    protected static Guid OwnerId => Owner.ObjectId;

    protected async Task<FolderDto> NewFolderAsync(string name, Guid? parentId = null) =>
        Require(await OwnerVault.CreateFolderAsync(new CreateFolderRequestDto { Name = name, ParentId = parentId }));

    protected Task<SecretDto> NewSecretAsync(PlaintextMarkers? markers = null, Guid? folderId = null) =>
        CreateSecretAsync(OwnerVault, markers ?? new PlaintextMarkers(), folderId);

    protected async Task<ShareGrantDto> ShareSecretAsync(Guid secretId, TestUser recipient, SharePermissionDto permission) =>
        Require(await OwnerSharing.CreateSecretGrantAsync(OwnerId, secretId, GrantBody(recipient, permission)));

    protected async Task<ShareGrantDto> ShareFolderAsync(Guid folderId, TestUser recipient, SharePermissionDto permission) =>
        Require(await OwnerSharing.CreateFolderGrantAsync(OwnerId, folderId, GrantBody(recipient, permission)));

    protected static CreateShareGrantRequestDto GrantBody(TestUser recipient, SharePermissionDto permission) =>
        new() { RecipientId = recipient.ObjectId, Permission = permission };

    protected static async Task<string> RevealValueAsync(SharingTestApi recipient, Guid secretId, Guid? ownerId = null) =>
        Require(await recipient.RevealSharedSecretAsync(
            ownerId ?? OwnerId,
            secretId,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Value;

    protected async Task<string> OwnerRevealValueAsync(Guid secretId) =>
        Require(await OwnerVault.RevealSecretAsync(
            secretId,
            new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Value;

    protected static UpdateSecretRequestDto EditBody(string name, string? newValue = null) => new()
    {
        Type = SecretTypeDto.Login,
        Name = name,
        ReplaceValue = newValue is not null,
        Value = newValue,
    };

    protected static async Task<IReadOnlyList<SharedItemDto>> WholeSharedListAsync(SharingTestApi recipient, Guid? filedFolderId = null)
    {
        var items = new List<SharedItemDto>();
        string? token = null;
        do
        {
            var page = Require(await recipient.ListSharedItemsAsync(100, token, filedFolderId));
            items.AddRange(page.Items);
            token = page.ContinuationToken;
        }
        while (token is not null);

        return items;
    }

    protected static async Task<IReadOnlyList<SecretDto>> WholeSharedFolderSecretsAsync(SharingTestApi recipient, Guid ownerId, Guid folderId, int pageSize = 100)
    {
        var items = new List<SecretDto>();
        string? token = null;
        do
        {
            var page = Require(await recipient.GetSharedFolderAsync(ownerId, folderId, pageSize, token));
            items.AddRange(page.Items);
            token = page.ContinuationToken;
        }
        while (token is not null);

        return items;
    }

    protected static async Task AssertSecretStatusAsync(HttpStatusCode expected, SharingTestApi recipient, Guid secretId, Guid? ownerId = null)
    {
        var result = await recipient.GetSharedSecretAsync(ownerId ?? OwnerId, secretId);
        Assert.Equal(expected, result.Response.StatusCode);
    }

    protected static async Task AssertFolderStatusAsync(HttpStatusCode expected, SharingTestApi recipient, Guid folderId, Guid? ownerId = null)
    {
        var result = await recipient.GetSharedFolderAsync(ownerId ?? OwnerId, folderId);
        Assert.Equal(expected, result.Response.StatusCode);
    }

    protected Task UnlockUserAsync(Guid oid) => SetUserStatusAsync(oid, UserRows.ActiveStatus);
}
