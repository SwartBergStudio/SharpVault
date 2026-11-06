// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingLeakageSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    public static TheoryData<string> UnknownVersusForbiddenRoutes() =>
    [
        "get",
        "update",
        "reveal",
        "folder",
        "file-secret",
        "file-folder",
        "list-secret-grants",
        "create-secret-grant",
        "change-secret-grant",
        "revoke-secret-grant",
        "list-folder-grants",
        "create-folder-grant",
    ];

    private static async Task<HttpResponseMessage> SendAsync(SharingTestApi api, string route, Guid owner, Guid item, Guid recipient, string token) => route switch
    {
        "get" => (await api.GetSharedSecretAsync(owner, item)).Response,
        "update" => (await api.UpdateSharedSecretAsync(owner, item, token, RenameBody("probe"))).Response,
        "reveal" => (await api.RevealSharedSecretAsync(owner, item, ViewValue)).Response,
        "folder" => (await api.GetSharedFolderAsync(owner, item)).Response,
        "file-secret" => await api.FileSharedSecretAsync(owner, item, new FileSharedItemRequestDto { FolderId = null }),
        "file-folder" => await api.FileSharedFolderAsync(owner, item, new FileSharedItemRequestDto { FolderId = null }),
        "list-secret-grants" => (await api.ListSecretGrantsAsync(owner, item)).Response,
        "create-secret-grant" => (await api.CreateSecretGrantAsync(owner, item, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response,
        "change-secret-grant" => (await api.ChangeSecretGrantAsync(owner, item, recipient, token, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit })).Response,
        "revoke-secret-grant" => await api.RevokeSecretGrantAsync(owner, item, recipient),
        "list-folder-grants" => (await api.ListFolderGrantsAsync(owner, item)).Response,
        "create-folder-grant" => (await api.CreateFolderGrantAsync(owner, item, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response,
        _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
    };

    private static Guid ItemFor(string route, SharingScenario scenario) =>
        route is "folder" or "file-folder" or "list-folder-grants" or "create-folder-grant" ? scenario.FolderF : scenario.SecretS;

    [Fact]
    public async Task SharedList_Responses_ContainNoValueOrNotesMarker()
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);

        foreach (var caller in new[] { TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager, TestUsers.FolderViewer, TestUsers.FolderManager })
        {
            var api = SharingFor(caller);
            var responses = new List<HttpResponseMessage>
            {
                (await api.ListSharedItemsAsync()).Response,
                (await api.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretS)).Response,
                (await api.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretT)).Response,
                (await api.GetSharedFolderAsync(scenario.OwnerId, scenario.FolderF)).Response,
                (await api.GetSharedFolderAsync(scenario.OwnerId, scenario.FolderG)).Response,
            };
            foreach (var response in responses)
            {
                var text = await response.Content.ReadAsStringAsync(Token);
                foreach (var markers in scenario.Markers)
                {
                    Assert.DoesNotContain(markers.Value, text, StringComparison.Ordinal);
                    Assert.DoesNotContain(markers.Notes, text, StringComparison.Ordinal);
                }
            }
        }
    }

    [Fact]
    public async Task SharedGet_FolderIdIsNull()
    {
        var folder = await NewFolderAsync("hidden-structure");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        foreach (var caller in new[] { TestUsers.ShareViewer, TestUsers.FolderViewer })
        {
            var shared = Require(await SharingFor(caller).GetSharedSecretAsync(Owner.ObjectId, secret.Id));
            Assert.Null(shared.Item.FolderId);
        }
    }

    [Fact]
    public async Task SharedUpdate_ResponseFolderIdIsNull()
    {
        var folder = await NewFolderAsync("hidden-structure");
        var secret = await NewSecretAsync(new PlaintextMarkers(), folder.Id);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);

        var updated = Require(await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("renamed")));

        Assert.Null(updated.FolderId);
        Assert.Equal(folder.Id, Require(await ApiFor(Owner).GetSecretAsync(secret.Id)).FolderId);
    }

    [Fact]
    public async Task SharedFolder_PathStopsAtHighestGrantedAncestor()
    {
        var top = new PlaintextMarkers();
        var middle = new PlaintextMarkers();
        var bottom = new PlaintextMarkers();
        var a = await NewFolderAsync(top.Folder);
        var b = await NewFolderAsync(middle.Folder, a.Id);
        var c = await NewFolderAsync(bottom.Folder, b.Id);
        await ShareFolderAsync(b.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        var response = await SharingFor(TestUsers.FolderViewer).GetSharedFolderAsync(Owner.ObjectId, c.Id);

        Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
        var text = await response.Response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain(top.Folder, text, StringComparison.Ordinal);
        Assert.DoesNotContain(a.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal([b.Id, c.Id], response.Body!.Path.Select(step => step.FolderId));
    }

    [Fact]
    public async Task SharedFolder_SiblingFolderNamesNeverReturned()
    {
        var sibling = new PlaintextMarkers();
        var rootSibling = new PlaintextMarkers();
        var parent = await NewFolderAsync("parent");
        var shared = await NewFolderAsync("shared", parent.Id);
        await NewFolderAsync(sibling.Folder, parent.Id);
        await NewFolderAsync(rootSibling.Folder);
        await NewFolderAsync("shared-child", shared.Id);
        await ShareFolderAsync(shared.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var api = SharingFor(TestUsers.FolderViewer);

        var texts = new List<string>
        {
            await (await api.GetSharedFolderAsync(Owner.ObjectId, shared.Id)).Response.Content.ReadAsStringAsync(Token),
            await (await api.ListSharedItemsAsync()).Response.Content.ReadAsStringAsync(Token),
        };

        foreach (var text in texts)
        {
            Assert.DoesNotContain(sibling.Folder, text, StringComparison.Ordinal);
            Assert.DoesNotContain(rootSibling.Folder, text, StringComparison.Ordinal);
            Assert.DoesNotContain(parent.Id.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task SharedFolder_UngrantedChildOfGrantedParentIsCovered()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        await ShareFolderAsync(parent.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        var response = await SharingFor(TestUsers.FolderViewer).GetSharedFolderAsync(Owner.ObjectId, child.Id);

        Assert.Equal(HttpStatusCode.OK, response.Response.StatusCode);
        Assert.Equal(child.Id, response.Body!.FolderId);
    }

    [Fact]
    public async Task SharedFolder_ParentOfGrantedChild_Returns404()
    {
        var parent = await NewFolderAsync("parent");
        var child = await NewFolderAsync("child", parent.Id);
        await ShareFolderAsync(child.Id, TestUsers.FolderViewer, SharePermissionDto.View);

        Assert.Equal(HttpStatusCode.NotFound, await SharedFolderStatusAsync(TestUsers.FolderViewer, parent.Id));
        Assert.Equal(HttpStatusCode.OK, await SharedFolderStatusAsync(TestUsers.FolderViewer, child.Id));
    }

    [Theory]
    [InlineData(CallerKind.ShareViewer)]
    [InlineData(CallerKind.ShareManager)]
    [InlineData(CallerKind.FolderViewer)]
    public async Task Recipient_HistoryRoutes_Return404(CallerKind caller)
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        var ownerApi = ApiFor(Owner);
        var versionId = Require(await ownerApi.ListHistoryAsync(scenario.SecretS)).Items.FirstOrDefault()?.VersionId ?? Guid.NewGuid();
        using var client = await ArrangeCallerAsync(caller);
        var api = new VaultTestApi(client);

        var responses = new List<HttpResponseMessage>
        {
            (await api.ListHistoryAsync(scenario.SecretS)).Response,
            (await api.ListHistoryAsync(scenario.SecretT)).Response,
            (await api.RevealVersionAsync(scenario.SecretS, versionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response,
            (await api.RestoreVersionAsync(scenario.SecretS, versionId, scenario.SecretToken)).Response,
        };

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
    }

    [Theory]
    [MemberData(nameof(UnknownVersusForbiddenRoutes))]
    public async Task SharedRoutes_UnknownVersusForbidden_IdenticalBodies(string route)
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        await WarmUpAsync(TestUsers.OtherUser);
        var api = SharingFor(TestUsers.OtherUser);
        var item = ItemFor(route, scenario);

        var real = await SendAsync(api, route, scenario.OwnerId, item, TestUsers.ShareViewer.ObjectId, scenario.SecretToken);
        var random = await SendAsync(api, route, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), scenario.SecretToken);
        var realOwnerRandomItem = await SendAsync(api, route, scenario.OwnerId, Guid.NewGuid(), TestUsers.ShareViewer.ObjectId, scenario.SecretToken);

        Assert.Equal(HttpStatusCode.NotFound, real.StatusCode);
        Assert.Equal(real.StatusCode, random.StatusCode);
        Assert.Equal(real.StatusCode, realOwnerRandomItem.StatusCode);
        var realBody = await NormalisedAsync(real);
        Assert.Equal(await NormalisedAsync(random), realBody);
        Assert.Equal(await NormalisedAsync(realOwnerRandomItem), realBody);
        Assert.Equal(random.Content.Headers.ContentType, real.Content.Headers.ContentType);
        Assert.Equal(random.Headers.CacheControl?.ToString(), real.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task ListGrants_LockedRecipient_LooksIdenticalToActive()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.LockedRecipient, SharePermissionDto.View);
        await WarmUpAsync(TestUsers.LockedRecipient);
        var before = await ShapeOfGrantsAsync(secret.Id);

        await LockUserAsync(TestUsers.LockedRecipient.ObjectId);
        var after = await ShapeOfGrantsAsync(secret.Id);

        Assert.Equal(before, after);
        Assert.Equal(2, after.Count);
        Assert.Single(after.Distinct());
    }

    private async Task<IReadOnlyList<string>> ShapeOfGrantsAsync(Guid secretId)
    {
        var response = (await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secretId)).Response;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var shapes = new List<string>();
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var shape = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in item.EnumerateObject())
            {
                shape[property.Name] = property.Name is "recipientId" or "recipientDisplayName" or "recipientEmail" or "concurrencyToken" or "grantedAt" or "modifiedAt"
                    ? property.Value.ValueKind.ToString()
                    : property.Value.ToString();
            }

            shapes.Add(string.Join('|', shape.Select(pair => pair.Key + "=" + pair.Value)));
        }

        return [.. shapes.Order(StringComparer.Ordinal)];
    }

    [Fact]
    public async Task CreateGrant_UnknownLockedGuestOwnerSelf_IdenticalResponses()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await WarmUpAsync(TestUsers.LockedUser);
        await LockUserAsync(TestUsers.LockedUser.ObjectId);
        var recipients = new[]
        {
            Guid.NewGuid(),
            TestUsers.LockedUser.ObjectId,
            TestUsers.Guest.ObjectId,
            Owner.ObjectId,
            TestUsers.OtherTenantUser.ObjectId,
        };
        var api = SharingFor(Owner);
        var bodies = new List<string>();
        var statuses = new List<HttpStatusCode>();

        foreach (var recipient in recipients)
        {
            var response = (await api.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, new CreateShareGrantRequestDto { RecipientId = recipient, Permission = SharePermissionDto.View })).Response;
            statuses.Add(response.StatusCode);
            bodies.Add(await NormalisedAsync(response));
        }

        Assert.All(statuses, status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Single(bodies.Distinct());
        Assert.Empty((await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Body!.Items);
    }

    [Fact]
    public async Task CreateGrant_NotManager_MakesNoDirectoryCall()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        Factory.GraphDirectory.Clear();

        foreach (var caller in new[] { TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.OtherUser, TestUsers.Admin })
        {
            var response = (await SharingFor(caller).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.FolderViewer, SharePermissionDto.View))).Response;
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        Assert.Equal(0, Factory.GraphDirectory.Calls);
    }

    [Fact]
    public async Task DirectoryEndpoint_HasNoContinuation()
    {
        await WarmUpAsync(Owner);

        var response = (await SharingFor(Owner).SearchDirectoryAsync("sharpvault")).Response;
        var withPaging = await ClientFor(Owner).GetAsync($"{ApiRoutes.DirectoryUsers}?search=sharpvault&{ApiQuery.ContinuationToken}=abc&{ApiQuery.PageSize}=1", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withPaging.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(["items"], document.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(await response.Content.ReadAsStringAsync(Token), await withPaging.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task SearchDirectory_NoMatchAndOnlyLockedMatch_IdenticalBodies()
    {
        await WarmUpAsync(TestUsers.LockedRecipient);
        await LockUserAsync(TestUsers.LockedRecipient.ObjectId);
        var api = SharingFor(Owner);

        var onlyLocked = (await api.SearchDirectoryAsync("locked-recipient")).Response;
        var none = (await api.SearchDirectoryAsync("no-such-person-anywhere")).Response;

        Assert.Equal(HttpStatusCode.OK, onlyLocked.StatusCode);
        Assert.Equal(none.StatusCode, onlyLocked.StatusCode);
        Assert.Equal(await NormalisedAsync(none), await NormalisedAsync(onlyLocked));
        Assert.Equal(none.Content.Headers.ContentLength, onlyLocked.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task SearchDirectory_ReturnsAtMostTen()
    {
        var (response, body) = await SharingFor(Owner).SearchDirectoryAsync("sharpvault");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, body!.Items.Count);
        Assert.DoesNotContain(body.Items, user => user.Id == Owner.ObjectId);
    }

    [Fact]
    public async Task Sharing_ErrorBodies_HaveNoStackOrInternalIds()
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        var viewer = SharingFor(TestUsers.ShareViewer);
        var owner = SharingFor(Owner);
        var responses = new List<HttpResponseMessage>
        {
            (await viewer.GetSharedSecretAsync(scenario.OwnerId, scenario.SecretT)).Response,
            (await viewer.UpdateSharedSecretAsync(scenario.OwnerId, scenario.SecretS, scenario.SecretToken, RenameBody("x"))).Response,
            (await owner.CreateSecretGrantAsync(scenario.OwnerId, scenario.SecretS, GrantBody(Owner, SharePermissionDto.View))).Response,
            (await owner.ChangeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareViewer.ObjectId, "\"W/x\"", new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit })).Response,
            (await owner.ChangeSecretGrantAsync(scenario.OwnerId, scenario.SecretS, TestUsers.ShareViewer.ObjectId, null, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit })).Response,
            (await owner.SearchDirectoryAsync("ab")).Response,
            (await viewer.ListSharedItemsAsync(continuationToken: "bad token")).Response,
        };

        foreach (var response in responses)
        {
            Assert.False(response.IsSuccessStatusCode);
            var text = await response.Content.ReadAsStringAsync(Token);
            Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
            Assert.DoesNotContain("SharpVault.", text, StringComparison.Ordinal);
            Assert.DoesNotContain(scenario.FolderF.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(scenario.SecretT.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(TestUsers.ShareManager.ObjectId.ToString("D"), text, StringComparison.OrdinalIgnoreCase);
            foreach (var markers in scenario.Markers)
            {
                markers.AssertAbsentFrom(text);
            }
        }
    }
}
