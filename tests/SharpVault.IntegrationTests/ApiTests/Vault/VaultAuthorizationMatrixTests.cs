// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Azure.Data.Tables;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Storage;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultAuthorizationMatrixTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const string RequestName = "matrix-request-name";

    private static readonly CallerKind[] DeniedCallers =
    [
        CallerKind.Anonymous,
        CallerKind.AlgNoneToken,
        CallerKind.UnknownKeyToken,
        CallerKind.OtherTenantUser,
        CallerKind.Guest,
        CallerKind.AuthenticatedWithoutRole,
        CallerKind.LockedUser,
        CallerKind.LockedAdmin,
    ];

    private static readonly CallerKind[] NonOwnerCallers = [CallerKind.OtherUser, CallerKind.Admin];

    private static readonly IReadOnlyList<MatrixEndpoint> Endpoints =
    [
        new("E1", (api, ids) => Response(api.ListSecretsAsync()), HttpStatusCode.OK, HttpStatusCode.OK),
        new("E2", (api, ids) => Response(api.GetSecretAsync(ids.SecretS)), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E3", (api, ids) => Response(api.CreateSecretAsync(NewSecret(null))), HttpStatusCode.Created, HttpStatusCode.Created),
        new("E3-folder", (api, ids) => Response(api.CreateSecretAsync(NewSecret(ids.FolderF))), HttpStatusCode.Created, HttpStatusCode.NotFound),
        new("E4", (api, ids) => Response(api.UpdateSecretAsync(ids.SecretS, ids.SecretToken, UpdateBody())), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E5", (api, ids) => Response(api.MoveSecretAsync(ids.SecretS, ids.SecretToken, new MoveSecretRequestDto { FolderId = null })), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E6", (api, ids) => api.DeleteSecretAsync(ids.SecretS, ids.SecretToken), HttpStatusCode.NoContent, HttpStatusCode.NotFound),
        new("E7", (api, ids) => Response(api.RevealSecretAsync(ids.SecretS, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E8", (api, ids) => Response(api.ListHistoryAsync(ids.SecretS)), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E9", (api, ids) => Response(api.RevealVersionAsync(ids.SecretS, ids.VersionV, new RevealVersionRequestDto { Intent = RevealIntentDto.View })), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E10", (api, ids) => Response(api.RestoreVersionAsync(ids.SecretS, ids.VersionV, ids.SecretToken)), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E11", (api, ids) => Response(api.ListFoldersAsync()), HttpStatusCode.OK, HttpStatusCode.OK),
        new("E12", (api, ids) => Response(api.CreateFolderAsync(new CreateFolderRequestDto { Name = RequestName })), HttpStatusCode.Created, HttpStatusCode.Created),
        new("E12-parent", (api, ids) => Response(api.CreateFolderAsync(new CreateFolderRequestDto { Name = RequestName, ParentId = ids.FolderF })), HttpStatusCode.Created, HttpStatusCode.NotFound),
        new("E13", (api, ids) => Response(api.RenameFolderAsync(ids.FolderF, new RenameFolderRequestDto { Name = RequestName })), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E14", (api, ids) => Response(api.MoveFolderAsync(ids.FolderG, new MoveFolderRequestDto { ParentId = null })), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E15-move", (api, ids) => api.DeleteFolderAsync(ids.FolderG, FolderDeleteModeDto.MoveToParent), HttpStatusCode.NoContent, HttpStatusCode.NotFound),
        new("E15-delete", (api, ids) => api.DeleteFolderAsync(ids.FolderG, FolderDeleteModeDto.DeleteContents), HttpStatusCode.NoContent, HttpStatusCode.NotFound),
        new("E16", (api, ids) => Response(api.ListRecycleBinAsync()), HttpStatusCode.OK, HttpStatusCode.OK),
        new("E17", (api, ids) => Response(api.RestoreRecycledSecretAsync(ids.BinnedSecretB)), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E18", (api, ids) => Response(api.RestoreRecycledFolderAsync(ids.BinnedFolderC)), HttpStatusCode.OK, HttpStatusCode.NotFound),
        new("E19", (api, ids) => api.PurgeRecycledSecretAsync(ids.BinnedSecretB), HttpStatusCode.NoContent, HttpStatusCode.NotFound),
        new("E20", (api, ids) => api.PurgeRecycledFolderAsync(ids.BinnedFolderC), HttpStatusCode.NoContent, HttpStatusCode.NotFound),
    ];

    public static TheoryData<string> EndpointIds() => [.. Endpoints.Select(endpoint => endpoint.Id)];

    public static TheoryData<string, CallerKind, HttpStatusCode> MatrixCases()
    {
        var cases = new TheoryData<string, CallerKind, HttpStatusCode>();
        foreach (var endpoint in Endpoints)
        {
            foreach (var caller in DeniedCallers)
            {
                cases.Add(endpoint.Id, caller, ExpectedDenied(caller));
            }

            cases.Add(endpoint.Id, CallerKind.User, endpoint.OwnerStatus);
            cases.Add(endpoint.Id, CallerKind.OtherUser, endpoint.NonOwnerStatus);
            cases.Add(endpoint.Id, CallerKind.Admin, endpoint.NonOwnerStatus);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(MatrixCases))]
    public async Task VaultAuthorizationMatrix_EveryEndpointAndCaller_ReturnsExpectedStatus(string endpointId, CallerKind caller, HttpStatusCode expected)
    {
        var ids = await ArrangeAsync();
        using var client = await ArrangeCallerAsync(caller);

        var response = await Endpoint(endpointId).Send(new VaultTestApi(client), ids);

        Assert.Equal(expected, response.StatusCode);
        await AssertBodyRevealsNothingAsync(response, ids, caller);
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task VaultAuthorizationMatrix_LockedOwner_ReturnsForbidden(string endpointId)
    {
        var ids = await ArrangeAsync();
        await LockUserAsync(TestUsers.User.ObjectId);
        using var client = ClientFor(TestUsers.User);

        var response = await Endpoint(endpointId).Send(new VaultTestApi(client), ids);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertBodyRevealsNothingAsync(response, ids, CallerKind.LockedUser);
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task VaultAuthorizationMatrix_DeniedCallers_MakeNoVaultStorageOrKeyVaultCalls(string endpointId)
    {
        var ids = await ArrangeAsync();
        var endpoint = Endpoint(endpointId);
        var clients = new List<HttpClient>();
        foreach (var caller in DeniedCallers)
        {
            clients.Add(await ArrangeCallerAsync(caller));
        }

        await LockUserAsync(TestUsers.User.ObjectId);
        clients.Add(ClientFor(TestUsers.User));
        ResetCounters();

        foreach (var client in clients)
        {
            var response = await endpoint.Send(new VaultTestApi(client), ids);
            Assert.True(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, $"Unexpected status {response.StatusCode}.");
        }

        Assert.Empty(StorageRequests.VaultRequests());
        Assert.Equal(0, Factory.KeyWraps.Unwraps);
        Assert.Equal(0, Factory.KeyWraps.Wraps);
        clients.ForEach(client => client.Dispose());
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task VaultAuthorizationMatrix_NonOwners_LeaveOwnerDataUnchanged(string endpointId)
    {
        var ids = await ArrangeAsync();
        var endpoint = Endpoint(endpointId);
        var before = await OwnerSnapshotAsync();

        foreach (var caller in NonOwnerCallers)
        {
            using var client = await ArrangeCallerAsync(caller);
            await endpoint.Send(new VaultTestApi(client), ids);
        }

        var after = await OwnerSnapshotAsync();
        Assert.Equal(before.Rows, after.Rows);
        Assert.Equal(before.OwnerAuditRows, after.OwnerAuditRows);
    }

    [Theory]
    [InlineData("E1")]
    [InlineData("E11")]
    [InlineData("E16")]
    public async Task VaultAuthorizationMatrix_OtherUserLists_ContainNoneOfOwnersIds(string endpointId)
    {
        var ids = await ArrangeAsync();
        var endpoint = Endpoint(endpointId);

        foreach (var caller in NonOwnerCallers)
        {
            using var client = await ArrangeCallerAsync(caller);
            var response = await endpoint.Send(new VaultTestApi(client), ids);
            var body = await response.Content.ReadAsStringAsync(Token);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            foreach (var id in ids.OwnerIds)
            {
                Assert.DoesNotContain(id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(id.ToString("N"), body, StringComparison.OrdinalIgnoreCase);
            }

            ids.Markers.AssertAbsentFrom(body);
        }
    }

    [Theory]
    [MemberData(nameof(EndpointIds))]
    public async Task VaultAuthorizationMatrix_EveryEndpoint_NoQueryWithoutPartitionKey(string endpointId)
    {
        var ids = await ArrangeAsync();
        var endpoint = Endpoint(endpointId);
        ResetCounters();

        foreach (var caller in NonOwnerCallers.Concat([CallerKind.User]))
        {
            using var client = await ArrangeCallerAsync(caller);
            await endpoint.Send(new VaultTestApi(client), ids);
        }

        Assert.NotEmpty(StorageRequests.VaultRequests());
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    private static HttpStatusCode ExpectedDenied(CallerKind caller) => caller switch
    {
        CallerKind.Anonymous or CallerKind.AlgNoneToken or CallerKind.UnknownKeyToken or CallerKind.OtherTenantUser => HttpStatusCode.Unauthorized,
        _ => HttpStatusCode.Forbidden,
    };

    private static MatrixEndpoint Endpoint(string id) => Endpoints.Single(endpoint => endpoint.Id == id);

    private static async Task<HttpResponseMessage> Response<T>(Task<(HttpResponseMessage Response, T? Body)> call) => (await call).Response;

    private static CreateSecretRequestDto NewSecret(Guid? folderId) => new()
    {
        Type = SecretTypeDto.Login,
        Name = RequestName,
        Value = "matrix-request-value",
        FolderId = folderId,
    };

    private static UpdateSecretRequestDto UpdateBody() => new() { Type = SecretTypeDto.Login, Name = RequestName };

    private static async Task AssertBodyRevealsNothingAsync(HttpResponseMessage response, MatrixIds ids, CallerKind caller)
    {
        var body = await response.Content.ReadAsStringAsync(Token);
        if (caller is CallerKind.User)
        {
            return;
        }

        ids.Markers.AssertAbsentFrom(body);
        foreach (var id in ids.OwnerIds)
        {
            Assert.DoesNotContain(id.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("stack", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<MatrixIds> ArrangeAsync()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();

        var folderF = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-F" }));
        var folderG = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-G", ParentId = folderF.Id }));
        var secret = Require(await api.CreateSecretAsync(new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            Value = markers.Value,
            Notes = markers.Notes,
            FolderId = folderF.Id,
        }));
        Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, new UpdateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            Username = markers.Username,
            Url = markers.Url,
            Tags = [markers.Tag],
            ReplaceValue = true,
            Value = markers.Value + "-v2",
        }));
        var history = Require(await api.ListHistoryAsync(secret.Id));
        var version = Assert.Single(history.Items);
        var current = Require(await api.GetSecretAsync(secret.Id));

        var binned = Require(await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name + "-binned", Value = "binned" }));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteSecretAsync(binned.Id, binned.ConcurrencyToken)).StatusCode);
        var binnedFolder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder + "-C" }));
        Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteFolderAsync(binnedFolder.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);

        return new MatrixIds(markers, folderF.Id, folderG.Id, secret.Id, version.VersionId, binned.Id, binnedFolder.Id, current.ConcurrencyToken);
    }

    private async Task<OwnerSnapshot> OwnerSnapshotAsync()
    {
        var owner = TestUsers.User.ObjectId;
        var rows = await VaultRows.ListPartitionAsync(VaultsTable, owner, Token);
        var snapshot = rows.Select(row => $"{row.RowKey}|{row.ETag}").Order(StringComparer.Ordinal).ToList();
        var ownerText = owner.ToString("D");
        var auditRows = (await ListAuditRowsAsync()).Count(row => row.GetString("OwnerId") == ownerText);
        var secretIndexFilter = TableClient.CreateQueryFilter($"PartitionKey eq {SecretAuditKeys.PartitionKey(owner)}");
        var indexRows = 0;
        await foreach (var _ in GetTableClient(TableNames.SecretAuditLog).QueryAsync<TableEntity>(secretIndexFilter, cancellationToken: Token))
        {
            indexRows++;
        }

        return new OwnerSnapshot(snapshot, auditRows + indexRows);
    }


    private sealed record MatrixEndpoint(string Id, Func<VaultTestApi, MatrixIds, Task<HttpResponseMessage>> Send, HttpStatusCode OwnerStatus, HttpStatusCode NonOwnerStatus);

    private sealed record OwnerSnapshot(IReadOnlyList<string> Rows, int OwnerAuditRows);

    private sealed record MatrixIds(
        PlaintextMarkers Markers,
        Guid FolderF,
        Guid FolderG,
        Guid SecretS,
        Guid VersionV,
        Guid BinnedSecretB,
        Guid BinnedFolderC,
        string SecretToken)
    {
        public IReadOnlyList<Guid> OwnerIds => [FolderF, FolderG, SecretS, VersionV, BinnedSecretB, BinnedFolderC];
    }
}
