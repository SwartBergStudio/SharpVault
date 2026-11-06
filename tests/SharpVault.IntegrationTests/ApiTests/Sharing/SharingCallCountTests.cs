// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Performance")]
public sealed class SharingCallCountTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    private const int GateUsersRead = 1;

    private sealed record Counts(int VaultReads, int VaultWrites, int AuditWrites, int InboxRequests, int UsersReads, int Unwraps, int Wraps, int GraphCalls);

    private Counts Measure() => new(
        StorageRequests.Requests.Count(request => request.Method == "GET" && request.Path.Contains("/Vaults", StringComparison.Ordinal)),
        StorageRequests.Requests.Count(request => request.Method != "GET" && (request.Path.Contains("/Vaults", StringComparison.Ordinal) || request.Path.EndsWith("$batch", StringComparison.Ordinal))),
        StorageRequests.AuditWrites().Count,
        StorageRequests.ShareInboxRequests().Count,
        StorageRequests.Requests.Count(request => request.Method == "GET" && request.Path.Contains("/Users", StringComparison.Ordinal)),
        Factory.KeyWraps.Unwraps,
        Factory.KeyWraps.Wraps,
        Factory.GraphDirectory.Calls);

    private void Reset()
    {
        ResetCounters();
        Factory.GraphDirectory.Clear();
    }

    private async Task WarmUpAllAsync()
    {
        foreach (var user in new[] { Owner, TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager, TestUsers.FolderViewer, TestUsers.OtherUser })
        {
            await WarmUpAsync(user);
        }
    }

    private async Task<(Guid Top, Guid Deepest)> ChainAsync(int depth)
    {
        var top = await NewFolderAsync("chain-1");
        var current = top;
        for (var level = 2; level <= depth; level++)
        {
            current = await NewFolderAsync($"chain-{level}", current.Id);
        }

        return (top.Id, current.Id);
    }

    [Fact]
    public async Task SharedGet_Direct_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Reset();

        var status = await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(4, 0, 0, 0, GateUsersRead + 1, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedGet_ThroughFolderChainOfThree_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var (top, deepest) = await ChainAsync(3);
        var secret = await NewSecretAsync(new PlaintextMarkers(), deepest);
        await ShareFolderAsync(top, TestUsers.FolderViewer, SharePermissionDto.View);
        Reset();

        var status = await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(8, 0, 0, 0, GateUsersRead + 1, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedGet_NoGrant_MakesExactlyTwoReads()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        Reset();
        var realOwner = await SharedGetStatusAsync(TestUsers.OtherUser, secret.Id);
        var realCounts = Measure();
        Reset();

        var randomOwner = await SharedGetStatusAsync(TestUsers.OtherUser, Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, realOwner);
        Assert.Equal(HttpStatusCode.NotFound, randomOwner);
        Assert.Equal(new Counts(2, 0, 0, 0, GateUsersRead, 0, 0, 0), realCounts);
        Assert.Equal(realCounts, Measure());
    }

    [Fact]
    public async Task SharedUpdate_Metadata_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        Reset();

        var status = (await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("renamed"))).Response.StatusCode;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(4, 1, 2, 0, GateUsersRead, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedUpdate_WithValue_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        Reset();

        var status = (await SharingFor(TestUsers.ShareEditor).UpdateSharedSecretAsync(
            Owner.ObjectId,
            secret.Id,
            secret.ConcurrencyToken,
            new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "renamed", ReplaceValue = true, Value = "new-value" })).Response.StatusCode;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(5, 1, 2, 0, GateUsersRead, 2, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedReveal_Direct_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Reset();

        var status = await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(3, 0, 2, 0, GateUsersRead, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedReveal_ThroughFolderChainOfThree_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var (top, deepest) = await ChainAsync(3);
        var secret = await NewSecretAsync(new PlaintextMarkers(), deepest);
        await ShareFolderAsync(top, TestUsers.FolderViewer, SharePermissionDto.View);
        Reset();

        var status = await SharedRevealStatusAsync(TestUsers.FolderViewer, secret.Id);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(7, 0, 2, 0, GateUsersRead, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedFolder_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var (top, deepest) = await ChainAsync(3);
        await NewSecretAsync(new PlaintextMarkers(), deepest);
        await ShareFolderAsync(top, TestUsers.FolderViewer, SharePermissionDto.View);
        Reset();

        var status = await SharedFolderStatusAsync(TestUsers.FolderViewer, deepest);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(new Counts(4, 0, 0, 0, GateUsersRead + 1, 1, 0, 0), Measure());
    }

    [Fact]
    public async Task SharedFolder_NoGrant_MakesOneRead()
    {
        await WarmUpAllAsync();
        var folder = await NewFolderAsync("folder");
        Reset();

        var status = await SharedFolderStatusAsync(TestUsers.OtherUser, folder.Id);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal(new Counts(1, 0, 0, 0, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task ListSharedItems_TwoOwners_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        const int perOwner = 5;
        foreach (var owner in new[] { Owner, TestUsers.OtherUser })
        {
            for (var index = 0; index < perOwner; index++)
            {
                var secret = await NewSecretAsync(new PlaintextMarkers(), owner: owner);
                await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View, by: owner, ownerId: owner.ObjectId);
            }
        }

        Reset();

        var (response, body) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2 * perOwner, body!.Items.Count);
        const int owners = 2;
        Assert.Equal(new Counts((2 * owners) + (owners * perOwner), 0, 0, 1, GateUsersRead + owners, owners, 0, 0), Measure());
    }

    [Fact]
    public async Task FileSharedItem_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var own = await NewFolderAsync("own", owner: TestUsers.ShareViewer);
        Reset();
        var filed = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = own.Id });
        var intoFolder = Measure();
        Reset();

        var unfiled = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = null });

        Assert.Equal(HttpStatusCode.NoContent, filed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unfiled.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 2, GateUsersRead, 0, 0, 0), intoFolder);
        Assert.Equal(new Counts(1, 0, 0, 2, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task ListGrants_Owner_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Reset();

        var response = (await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Counts(2, 0, 0, 0, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task ListGrants_Manager_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareManager, SharePermissionDto.Manage);
        Reset();

        var response = (await SharingFor(TestUsers.ShareManager).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Counts(4, 0, 0, 0, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task CreateGrant_Secret_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        Reset();

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Counts(2, 1, 2, 2, GateUsersRead + 1, 0, 0, 1), Measure());
    }

    [Fact]
    public async Task CreateGrant_Folder_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var folder = await NewFolderAsync("folder");
        Reset();

        var response = (await SharingFor(Owner).CreateFolderGrantAsync(Owner.ObjectId, folder.Id, GrantBody(TestUsers.FolderViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(new Counts(2, 1, 1, 2, GateUsersRead + 1, 0, 0, 1), Measure());
    }

    [Fact]
    public async Task ChangeGrant_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Reset();

        var response = (await SharingFor(Owner).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit })).Response;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new Counts(3, 1, 2, 0, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task RevokeGrant_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        Reset();

        var response = await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(3, 1, 2, 2, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task RevokeFolderGrant_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var folder = await NewFolderAsync("folder");
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        Reset();

        var response = await SharingFor(Owner).RevokeFolderGrantAsync(Owner.ObjectId, folder.Id, TestUsers.FolderViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(3, 1, 1, 2, GateUsersRead, 0, 0, 0), Measure());
    }

    [Fact]
    public async Task SearchDirectory_Entra_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        Reset();

        var (response, body) = await SharingFor(Owner).SearchDirectoryAsync("sharpvault");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, body!.Items.Count);
        var counts = Measure();
        Assert.Equal(1, counts.GraphCalls);
        Assert.Equal(0, counts.VaultReads);
        Assert.Equal(0, counts.InboxRequests);
        Assert.Equal(GateUsersRead + 10, counts.UsersReads);
    }

    [Fact]
    public async Task PurgeSecret_WithThreeGrants_Calls_MatchBudget()
    {
        await WarmUpAllAsync();
        var secret = await NewSecretAsync(new PlaintextMarkers());
        const int grants = 3;
        foreach (var recipient in new[] { TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager })
        {
            await ShareSecretAsync(secret.Id, recipient, SharePermissionDto.View);
        }

        await ApiFor(Owner).DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);
        Reset();

        var response = await ApiFor(Owner).PurgeRecycledSecretAsync(secret.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new Counts(3 + grants, 1, 2 + (2 * grants), 2 * grants, GateUsersRead, 0, 0, 0), Measure());
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task DeniedCallers_MakeNoVaultInboxOrKeyVaultCalls()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        using var anonymous = await ArrangeCallerAsync(CallerKind.Anonymous);
        using var guest = await ArrangeCallerAsync(CallerKind.Guest);
        Reset();

        var statuses = new[]
        {
            (await new SharingTestApi(anonymous).GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Response.StatusCode,
            (await new SharingTestApi(guest).GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Response.StatusCode,
            (await new SharingTestApi(anonymous).SearchDirectoryAsync("sharpvault")).Response.StatusCode,
        };

        Assert.All(statuses, status => Assert.True(status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden));
        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Empty(StorageRequests.ShareInboxRequests());
        Assert.Equal(0, Factory.KeyWraps.Unwraps);
        Assert.Equal(0, Factory.GraphDirectory.Calls);
    }

    [Fact]
    public async Task SharingLifecycle_NoQueryRunsWithoutAPartitionKey()
    {
        await WarmUpAllAsync();
        var (top, deepest) = await ChainAsync(4);
        var secret = await NewSecretAsync(new PlaintextMarkers(), deepest);
        await ShareFolderAsync(top, TestUsers.FolderViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.Edit);
        Reset();

        await SharingFor(TestUsers.FolderViewer).ListSharedItemsAsync();
        await SharingFor(TestUsers.FolderViewer).GetSharedFolderAsync(Owner.ObjectId, deepest);
        await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id);
        await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id);
        await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secret.Id);
        await SharingFor(Owner).SearchDirectoryAsync("sharpvault");
        await SharingFor(Owner).RevokeFolderGrantAsync(Owner.ObjectId, top, TestUsers.FolderViewer.ObjectId);

        Assert.NotEmpty(StorageRequests.Requests);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }
}
