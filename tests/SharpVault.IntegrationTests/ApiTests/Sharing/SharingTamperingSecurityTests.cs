// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using Azure.Data.Tables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Sharing;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingTamperingSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    private const string PermissionColumn = "Permission";
    private const string NameEnvelope = "NameEnvelope";
    private const string UsernameEnvelope = "UsernameEnvelope";
    private const string WrappedDataKey = "WrappedDataKey";
    private const int ParallelTrials = 5;

    public static TheoryData<string> UnusableIfMatch() => ["*", string.Empty, "W/\"abc\""];

    public static TheoryData<string> TamperedPermissions() => ["Owner", "4", "view", string.Empty, "Manage,Edit", "None", "0"];

    public static TheoryData<string> MalformedTokens() =>
    [
        "not-a-token",
        "zz",
        "0123456789abcdef0123456789abcdef",
        "0123456789abcdef0123456789abcdef_x_0123456789abcdef0123456789abcdef",
        "' or PartitionKey ne '",
        "../../etc/passwd",
        "S!!!",
        "%00",
    ];

    private (WebApplicationFactory<Program> Host, FaultProxy<IVaultStore> Control) FaultyVaultHost()
    {
        var inner = new TableVaultStore(new TableServiceClient(Factory.AzuriteConnectionString), NullLogger<TableVaultStore>.Instance);
        var control = FaultProxy.Wrap<IVaultStore>(inner, out var proxy);
        var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVaultStore>();
            services.AddSingleton(proxy);
        }));
        return (host, control);
    }

    private (WebApplicationFactory<Program> Host, FaultProxy<IShareInboxStore> Control) FaultyInboxHost()
    {
        var inner = new TableShareInboxStore(new TableServiceClient(Factory.AzuriteConnectionString), NullLogger<TableShareInboxStore>.Instance);
        var control = FaultProxy.Wrap<IShareInboxStore>(inner, out var proxy);
        var host = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IShareInboxStore>();
            services.AddSingleton(proxy);
        }));
        return (host, control);
    }

    private static HttpClient ClientOn(WebApplicationFactory<Program> host, TestUser user) => CreateClientFor(host, TestTokens.Create(user));

    private static string SecretGrantsPath(Guid secretId) =>
        VaultTestApi.Bind(ApiRoutes.SecretGrants, ("ownerId", TestUsers.User.ObjectId), ("secretId", secretId));

    private static string SecretGrantPath(Guid secretId, TestUser recipient) =>
        VaultTestApi.Bind(ApiRoutes.SecretGrant, ("ownerId", TestUsers.User.ObjectId), ("secretId", secretId), ("recipientId", recipient.ObjectId));

    private async Task<IReadOnlyList<string>> GrantRowKeysAsync(Guid secretId)
    {
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, Owner.ObjectId, Token);
        var marker = secretId.ToString("N");
        return [.. rows.Where(row => row.RowKey.StartsWith("gi_", StringComparison.Ordinal) || row.RowKey.StartsWith("gr_", StringComparison.Ordinal))
            .Where(row => row.RowKey.Contains(marker, StringComparison.Ordinal))
            .Select(row => row.RowKey)];
    }

    private async Task<int> InboxRowCountAsync(Guid recipient)
    {
        var count = 0;
        var filter = TableClient.CreateQueryFilter($"PartitionKey eq {recipient.ToString("D")}");
        await foreach (var _ in GetTableClient(TableNames.ShareInbox).QueryAsync<TableEntity>(filter, cancellationToken: Token))
        {
            count++;
        }

        return count;
    }

    private async Task<IReadOnlyList<TableEntity>> ShareAuditAsync(params AuditEventType[] types) => await VaultAuditRowsAsync(types);

    [Fact]
    public async Task ChangeGrant_StaleToken_Returns409AndChangesNothing()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        var api = SharingFor(Owner);
        Require(await api.ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }));
        var before = await SnapshotAsync();

        var stale = (await api.ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var after = await SnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.AuditRows, after.AuditRows);
        Assert.Equal(SharePermissionDto.Edit, Require(await SharingFor(TestUsers.ShareEditor).GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Permission);
    }

    [Theory]
    [MemberData(nameof(UnusableIfMatch))]
    public async Task ChangeGrant_WildcardOrMissingIfMatch_Returns400(string ifMatch)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        var before = await SnapshotAsync();

        var response = (await SharingFor(Owner).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, ifMatch, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task ChangeGrant_MissingIfMatch_Returns400()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        var before = await SnapshotAsync();

        var response = (await SharingFor(Owner).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, null, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
    }

    [Fact]
    public async Task AddShareGrant_BothRowsInOneTransaction()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await WarmUpAsync(Owner);
        ResetCounters();

        Require(await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View)));

        var batches = StorageRequests.VaultWrites().Where(request => request.Path.EndsWith("/$batch", StringComparison.Ordinal)).ToList();
        Assert.Single(batches);
        Assert.Equal(2, (await GrantRowKeysAsync(secret.Id)).Count);
    }

    [Fact]
    public async Task ChangeGrant_RecipientRowChangedBetween_Conflict()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
        var (host, control) = FaultyVaultHost();
        using var _ = host;
        var recipientRow = VaultKeys.GrantByRecipientRowKey(TestUsers.ShareEditor.ObjectId, ShareItemKind.Secret, secret.Id);
        control.Before = async (name, _) =>
        {
            if (name == "SubmitAsync")
            {
                await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, recipientRow, "Touched", 1, Token);
            }
        };
        using var client = ClientOn(host, Owner);

        var response = (await new SharingTestApi(client).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage })).Response;

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(SharePermissionDto.View, Require(await SharingFor(TestUsers.ShareEditor).GetSharedSecretAsync(Owner.ObjectId, secret.Id)).Permission);
        Assert.Empty(await ShareAuditAsync(AuditEventType.SecretShareChanged));
    }

    [Fact]
    public async Task ParallelCreateSameRecipient_OneSucceedsOneConflicts()
    {
        for (var trial = 0; trial < ParallelTrials; trial++)
        {
            var secret = await NewSecretAsync(new PlaintextMarkers());
            using var first = ClientFor(Owner);
            using var second = ClientFor(Owner);

            var results = await Task.WhenAll(
                first.PostAsJsonAsync(SecretGrantsPath(secret.Id), GrantBody(TestUsers.ShareViewer, SharePermissionDto.View), VaultTestApi.Json, Token),
                second.PostAsJsonAsync(SecretGrantsPath(secret.Id), GrantBody(TestUsers.ShareViewer, SharePermissionDto.Edit), VaultTestApi.Json, Token));

            var statuses = results.Select(result => result.StatusCode).Order().ToList();
            Assert.Equal([HttpStatusCode.Created, HttpStatusCode.Conflict], statuses);
            Assert.Equal(2, (await GrantRowKeysAsync(secret.Id)).Count);
            Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        }
    }

    [Fact]
    public async Task ParallelRevokeAndChange_EndsRevoked()
    {
        for (var trial = 0; trial < ParallelTrials; trial++)
        {
            var secret = await NewSecretAsync(new PlaintextMarkers());
            var grant = await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.View);
            using var revoker = ClientFor(Owner);
            using var changer = ClientFor(Owner);

            var revoke = revoker.DeleteAsync(SecretGrantPath(secret.Id, TestUsers.ShareEditor), Token);
            var change = new SharingTestApi(changer).ChangeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareEditor.ObjectId, grant.ConcurrencyToken, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage });
            await Task.WhenAll(revoke, change);

            Assert.Equal(HttpStatusCode.NoContent, (await revoke).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareEditor, secret.Id));
            Assert.Empty(await GrantRowKeysAsync(secret.Id));
        }
    }

    [Fact]
    public async Task RevokeAsync_ThreeConflicts_ReturnsConflictAndAccessStateIsConsistent()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var (host, control) = FaultyVaultHost();
        using var _ = host;
        var itemRow = VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, secret.Id, TestUsers.ShareViewer.ObjectId);
        var attempts = 0;
        control.Before = async (name, _) =>
        {
            if (name == "SubmitAsync")
            {
                attempts++;
                await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, itemRow, "Touched", attempts, Token);
            }
        };
        using var client = ClientOn(host, Owner);

        var response = await new SharingTestApi(client).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(3, attempts);
        Assert.Equal(2, (await GrantRowKeysAsync(secret.Id)).Count);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(1, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
        Assert.Empty(await ShareAuditAsync(AuditEventType.SecretShareRevoked));
    }

    [Theory]
    [MemberData(nameof(TamperedPermissions))]
    public async Task TamperedPermissionRow_TreatedAsNoAccess(string tampered)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var itemRow = VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, secret.Id, TestUsers.ShareViewer.ObjectId);
        var recipientRow = VaultKeys.GrantByRecipientRowKey(TestUsers.ShareViewer.ObjectId, ShareItemKind.Secret, secret.Id);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, itemRow, PermissionColumn, tampered, Token);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, recipientRow, PermissionColumn, tampered, Token);

        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await SharingFor(TestUsers.ShareViewer).UpdateSharedSecretAsync(Owner.ObjectId, secret.Id, secret.ConcurrencyToken, RenameBody("tampered"))).Response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await SharingFor(TestUsers.ShareViewer).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Response.StatusCode);
        var (listStatus, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Equal(HttpStatusCode.OK, listStatus.StatusCode);
        Assert.Empty(list!.Items);
    }

    [Theory]
    [MemberData(nameof(TamperedPermissions))]
    public async Task TamperedPermissionRow_OwnerListing_DoesNotFail500(string tampered)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(secret.Id, TestUsers.ShareEditor, SharePermissionDto.Edit);
        await TamperPermissionAsync(secret.Id, TestUsers.ShareViewer, tampered);

        var ownerList = await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secret.Id);

        Assert.Equal(HttpStatusCode.OK, ownerList.Response.StatusCode);
        Assert.DoesNotContain(ownerList.Body!.Items, item => item.RecipientId == TestUsers.ShareViewer.ObjectId);
        Assert.Contains(ownerList.Body.Items, item => item.RecipientId == TestUsers.ShareEditor.ObjectId);
    }

    [Theory]
    [MemberData(nameof(TamperedPermissions))]
    public async Task TamperedPermissionRow_OwnerCanStillRevoke(string tampered)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await TamperPermissionAsync(secret.Id, TestUsers.ShareViewer, tampered);

        var response = await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Equal(0, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
    }

    private async Task TamperPermissionAsync(Guid secretId, TestUser recipient, string tampered)
    {
        var itemRow = VaultKeys.GrantByItemRowKey(ShareItemKind.Secret, secretId, recipient.ObjectId);
        var recipientRow = VaultKeys.GrantByRecipientRowKey(recipient.ObjectId, ShareItemKind.Secret, secretId);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, itemRow, PermissionColumn, tampered, Token);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, recipientRow, PermissionColumn, tampered, Token);
    }

    [Fact]
    public async Task CrossOwnerEnvelope_CopiedIntoRecipientsPartition_FailsToDecrypt()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        var victimApi = ApiFor(TestUsers.OtherUser);
        await CreateSecretAsync(victimApi, new PlaintextMarkers());
        var rowKey = VaultKeys.SecretRowKey(secret.Id, recycled: false);
        await VaultRows.CopyRowToPartitionAsync(VaultsTable, Owner.ObjectId, rowKey, TestUsers.OtherUser.ObjectId, Token);

        var read = await victimApi.GetSecretAsync(secret.Id);
        var reveal = await victimApi.RevealSecretAsync(secret.Id, ViewValue);

        var text = await read.Response.Content.ReadAsStringAsync(Token) + await reveal.Response.Content.ReadAsStringAsync(Token);
        markers.AssertAbsentFrom(text);
        Assert.True(read.Response.StatusCode is HttpStatusCode.NotFound || read.Body!.IsDamaged, "The copied row was readable.");
        Assert.NotEqual(HttpStatusCode.OK, reveal.Response.StatusCode);
    }

    [Fact]
    public async Task SharedList_TamperedNameEnvelope_ShowsDamagedRow()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await VaultRows.SwapPropertiesAsync(VaultsTable, Owner.ObjectId, VaultKeys.SecretRowKey(secret.Id, recycled: false), NameEnvelope, UsernameEnvelope, Token);

        var (response, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(list!.Items);
        Assert.True(item.IsDamaged);
        Assert.Null(item.Name);
        markers.AssertAbsentFrom(await response.Content.ReadAsStringAsync(Token));
        var shared = await SharingFor(TestUsers.ShareViewer).GetSharedSecretAsync(Owner.ObjectId, secret.Id);
        Assert.Equal(HttpStatusCode.OK, shared.Response.StatusCode);
        Assert.True(shared.Body!.Item.IsDamaged);
    }

    [Fact]
    public async Task SharedList_OwnerRingUnreadable_ShowsDamagedNot500()
    {
        var markers = new PlaintextMarkers();
        var secret = await NewSecretAsync(markers);
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, VaultKeys.KeyRingRowKey, WrappedDataKey, new byte[64], Token);

        var (response, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(list!.Items);
        Assert.True(item.IsDamaged);
        markers.AssertAbsentFrom(await response.Content.ReadAsStringAsync(Token));
    }

    [Theory]
    [MemberData(nameof(MalformedTokens))]
    public async Task ListSharedItems_MalformedToken_Returns400(string token)
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await WarmUpAsync(TestUsers.ShareViewer);
        ResetCounters();

        var response = (await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync(continuationToken: token)).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StorageRequests.ShareInboxRequests());
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task ListSharedItems_TokenNamingOtherOwner_StaysInCallersPartition()
    {
        var mine = await NewSecretAsync(new PlaintextMarkers());
        var theirs = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(mine.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        await ShareSecretAsync(theirs.Id, TestUsers.OtherUser, SharePermissionDto.View);
        var token = $"{Guid.Empty:N}_s_{Guid.Empty:N}";
        ResetCounters();

        var (response, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync(continuationToken: token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.All(list!.Items, item => Assert.NotEqual(theirs.Id, item.ItemId));
        Assert.Contains(list.Items, item => item.ItemId == mine.Id);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        var viewerPartition = TestUsers.ShareViewer.ObjectId.ToString("D");
        Assert.All(StorageRequests.ShareInboxRequests().Where(request => request.IsEntityQuery), request => Assert.Contains(viewerPartition, request.Filter ?? string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListSharedItems_StorageRejectsToken_Returns400()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var token = ContinuationTokens.ForStorage("garbage-that-storage-rejects");

        var response = (await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync(continuationToken: token)).Response;

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"Unexpected {response.StatusCode}.");
    }

    [Fact]
    public async Task SharedFolder_TokenFromOtherFolder_ReturnsOnlyThisFolder()
    {
        var folderA = await NewFolderAsync("folder-a");
        var folderB = await NewFolderAsync("folder-b");
        var inA = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            inA.Add((await NewSecretAsync(new PlaintextMarkers(), folderA.Id)).Id);
            await NewSecretAsync(new PlaintextMarkers(), folderB.Id);
        }

        await ShareFolderAsync(folderA.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        await ShareFolderAsync(folderB.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var api = SharingFor(TestUsers.FolderViewer);
        var firstPage = Require(await api.GetSharedFolderAsync(Owner.ObjectId, folderA.Id, pageSize: 1));
        Assert.NotNull(firstPage.ContinuationToken);

        var (response, crossed) = await api.GetSharedFolderAsync(Owner.ObjectId, folderB.Id, pageSize: 10, continuationToken: firstPage.ContinuationToken);

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"Unexpected {response.StatusCode}.");
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.All(crossed!.Items, item => Assert.DoesNotContain(item.Id, inA));
            Assert.Equal(folderB.Id, crossed.FolderId);
        }
    }

    [Fact]
    public async Task CreateGrant_InboxWriteFails_NoGrantRowsAndNoAudit()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var (host, control) = FaultyInboxHost();
        using var _ = host;
        control.Before = (name, _) => name == "AddIfAbsentAsync" ? Task.FromException(new InvalidOperationException("inbox down")) : Task.CompletedTask;
        using var client = ClientOn(host, Owner);

        var response = (await new SharingTestApi(client).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Empty(await ShareAuditAsync(AuditEventType.SecretShared));
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(0, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
    }

    [Fact]
    public async Task CreateGrant_VaultWriteFails_OrphanInboxRowNotListedAndRepeatSucceeds()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var (host, control) = FaultyVaultHost();
        using (host)
        {
            control.Before = (name, _) => name == "SubmitAsync" ? Task.FromException(new InvalidOperationException("vault down")) : Task.CompletedTask;
            using var client = ClientOn(host, Owner);

            var failed = (await new SharingTestApi(client).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        }

        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Empty(await ShareAuditAsync(AuditEventType.SecretShared));
        Assert.Equal(1, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
        var (listResponse, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Empty(list!.Items);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));

        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);

        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Single((await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync()).Body!.Items);
    }

    [Fact]
    public async Task Revoke_InboxRemovalFails_Returns204AndAccessIsGone()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var (host, control) = FaultyInboxHost();
        using var _ = host;
        control.Before = (name, _) => name == "RemoveAsync" ? Task.FromException(new InvalidOperationException("inbox down")) : Task.CompletedTask;
        using var client = ClientOn(host, Owner);

        var response = await new SharingTestApi(client).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Equal(HttpStatusCode.NotFound, await SharedRevealStatusAsync(TestUsers.ShareViewer, secret.Id));
        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Single(await ShareAuditAsync(AuditEventType.SecretShareRevoked));
        Assert.Empty((await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync()).Body!.Items);
    }

    [Fact]
    public async Task Revoke_RepeatAfterInboxFailure_RemovesInboxRowAndReturns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var (host, control) = FaultyInboxHost();
        using (host)
        {
            control.Before = (name, _) => name == "RemoveAsync" ? Task.FromException(new InvalidOperationException("inbox down")) : Task.CompletedTask;
            using var client = ClientOn(host, Owner);
            await new SharingTestApi(client).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);
        }

        Assert.Equal(1, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));

        var repeat = await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        Assert.Equal(HttpStatusCode.NotFound, repeat.StatusCode);
        Assert.Equal(0, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
    }

    [Fact]
    public async Task CreateGrant_RequestAbortedAfterCommit_StillWritesAudit()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var (host, control) = FaultyVaultHost();
        using var _ = host;
        using var cts = new CancellationTokenSource();
        control.After = async (name, _) =>
        {
            if (name == "SubmitAsync")
            {
                await cts.CancelAsync();
                await Task.Delay(300, CancellationToken.None);
            }
        };
        using var client = ClientOn(host, Owner);

        await Record.ExceptionAsync(() => client.PostAsJsonAsync(SecretGrantsPath(secret.Id), GrantBody(TestUsers.ShareViewer, SharePermissionDto.View), VaultTestApi.Json, cts.Token));

        Assert.True(cts.IsCancellationRequested);
        Assert.Equal(2, (await GrantRowKeysAsync(secret.Id)).Count);
        Assert.Single(await EventuallyAsync(() => ShareAuditAsync(AuditEventType.SecretShared)));
    }

    [Fact]
    public async Task Revoke_RequestAbortedAfterCommit_StillWritesAudit()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var (host, control) = FaultyVaultHost();
        using var _ = host;
        using var cts = new CancellationTokenSource();
        control.After = async (name, _) =>
        {
            if (name == "SubmitAsync")
            {
                await cts.CancelAsync();
                await Task.Delay(300, CancellationToken.None);
            }
        };
        using var client = ClientOn(host, Owner);

        await Record.ExceptionAsync(() => client.DeleteAsync(SecretGrantPath(secret.Id, TestUsers.ShareViewer), cts.Token));

        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Single(await EventuallyAsync(() => ShareAuditAsync(AuditEventType.SecretShareRevoked)));
    }

    [Fact]
    public async Task PurgeSecret_WithGrants_RemovesGrantRowsAndInboxRows()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        var recipients = new[] { TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager };
        foreach (var recipient in recipients)
        {
            await ShareSecretAsync(secret.Id, recipient, SharePermissionDto.View);
        }

        var owner = ApiFor(Owner);
        await owner.DeleteSecretAsync(secret.Id, secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PurgeRecycledSecretAsync(secret.Id)).StatusCode);

        Assert.Empty(await GrantRowKeysAsync(secret.Id));
        Assert.Null(await VaultRows.GetAsync(VaultsTable, Owner.ObjectId, VaultKeys.SecretRowKey(secret.Id, recycled: true), Token));
        foreach (var recipient in recipients)
        {
            Assert.Equal(0, await InboxRowCountAsync(recipient.ObjectId));
        }

        var erased = await ShareAuditAsync(AuditEventType.ShareErased);
        Assert.Equal(recipients.Length, erased.Count);
    }

    [Fact]
    public async Task PurgeSecret_GrantsSpanTwoChunks_SecondFails_SecretRemainsAndRepeatFinishes()
    {
        _ = Factory.Server;
        await WarmUpAsync(Owner);
        var world = new PurgeGrantWorld(
            Factory.Services.GetRequiredService<IVaultStore>(),
            Factory.Services.GetRequiredService<IShareInboxStore>(),
            GetTableClient(TableNames.Vaults),
            GetTableClient(TableNames.ShareInbox),
            Owner.ObjectId);
        const int grantCount = 150;
        var (binned, grants) = await world.SeedRecycledSecretAsync(grantCount, 1, Token);
        var secretId = binned.Value.Id;
        var (host, control) = FaultyVaultHost();
        using (host)
        {
            control.Before = async (name, number) =>
            {
                if (name == "SubmitAsync" && number == 1)
                {
                    await VaultRows.SetPropertyAsync(VaultsTable, Owner.ObjectId, VaultKeys.SecretRowKey(secretId, recycled: true), "Touched", 1, Token);
                }
            };
            using var client = ClientOn(host, Owner);

            var failed = await new VaultTestApi(client).PurgeRecycledSecretAsync(secretId);
            Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        }

        Assert.NotNull(await VaultRows.GetAsync(VaultsTable, Owner.ObjectId, VaultKeys.SecretRowKey(secretId, recycled: true), Token));
        var remaining = await world.GrantsStillStoredAsync(grants, Token);
        Assert.Equal(0, remaining);
        var erasedAudit = await ShareAuditAsync(AuditEventType.ShareErased);
        Assert.Equal(grantCount - remaining, erasedAudit.Count);

        Assert.Equal(HttpStatusCode.NoContent, (await ApiFor(Owner).PurgeRecycledSecretAsync(secretId)).StatusCode);

        Assert.Null(await VaultRows.GetAsync(VaultsTable, Owner.ObjectId, VaultKeys.SecretRowKey(secretId, recycled: true), Token));
        Assert.Equal(0, await world.GrantsStillStoredAsync(grants, Token));
        Assert.Equal(0, await world.InboxRowCountAsync(Token));
        Assert.Equal(grantCount, (await ShareAuditAsync(AuditEventType.ShareErased)).Count);
    }

    [Fact]
    public async Task FileSharedItem_OtherUsersFolderId_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var foreignFolder = await NewFolderAsync("foreign", owner: TestUsers.OtherUser);

        var response = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = foreignFolder.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var (_, list) = await SharingFor(TestUsers.ShareViewer).ListSharedItemsAsync();
        Assert.Null(Assert.Single(list!.Items).FiledFolderId);
    }

    [Fact]
    public async Task FileSharedItem_DoesNotChangeAnyOwnerRow()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var ownFolder = await NewFolderAsync("recipient-folder", owner: TestUsers.ShareViewer);
        var before = await RowsOfAsync(Owner.ObjectId);
        var beforeAudit = (await SnapshotAsync()).AuditRows;

        var filed = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = ownFolder.Id });
        var unfiled = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = null });

        Assert.Equal(HttpStatusCode.NoContent, filed.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, unfiled.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner.ObjectId));
        Assert.Equal(beforeAudit, (await SnapshotAsync()).AuditRows);
        Assert.Equal(secret.FolderId, Require(await ApiFor(Owner).GetSecretAsync(secret.Id)).FolderId);
    }

    [Fact]
    public async Task FileSharedItem_Revoked_Returns404()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await ShareSecretAsync(secret.Id, TestUsers.ShareViewer, SharePermissionDto.View);
        var ownFolder = await NewFolderAsync("recipient-folder", owner: TestUsers.ShareViewer);
        await SharingFor(Owner).RevokeSecretGrantAsync(Owner.ObjectId, secret.Id, TestUsers.ShareViewer.ObjectId);

        var response = await SharingFor(TestUsers.ShareViewer).FileSharedSecretAsync(Owner.ObjectId, secret.Id, new FileSharedItemRequestDto { FolderId = ownFolder.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, await InboxRowCountAsync(TestUsers.ShareViewer.ObjectId));
    }

    private static async Task<IReadOnlyList<T>> EventuallyAsync<T>(Func<Task<IReadOnlyList<T>>> read)
    {
        IReadOnlyList<T> rows = [];
        for (var attempt = 0; attempt < 40; attempt++)
        {
            rows = await read();
            if (rows.Count > 0)
            {
                return rows;
            }

            await Task.Delay(100, Token);
        }

        return rows;
    }
}
