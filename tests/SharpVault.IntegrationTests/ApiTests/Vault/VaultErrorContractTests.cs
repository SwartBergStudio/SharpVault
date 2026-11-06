// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Audit;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Support;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed partial class VaultErrorContractTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const string DecryptionTitle = "The item could not be read.";
    private const string ValueEnvelope = "ValueEnvelope";
    private const string NotesEnvelope = "NotesEnvelope";
    private const string WrappedDataKey = "WrappedDataKey";
    private const string DataKeyAlgorithm = "DataKeyAlgorithm";

    private static readonly Dictionary<string, string> TamperCases = new()
    {
        ["swapped-envelopes"] = "swap",
        ["truncated-envelope"] = ValueEnvelope,
        ["bad-algorithm"] = DataKeyAlgorithm,
        ["bad-wrapped-key"] = WrappedDataKey,
    };

    public static TheoryData<string> TamperNames() => [.. TamperCases.Keys];

    [GeneratedRegex("\"traceId\":\"[^\"]*\"", RegexOptions.CultureInvariant)]
    private static partial Regex TraceId();

    private async Task TamperAsync(Guid owner, Guid secretId, string kind)
    {
        var rowKey = VaultKeys.SecretRowKey(secretId, recycled: false);
        switch (TamperCases[kind])
        {
            case "swap":
                await VaultRows.SwapPropertiesAsync(VaultsTable, owner, rowKey, ValueEnvelope, NotesEnvelope, Token);
                break;
            case ValueEnvelope:
                await VaultRows.SetPropertyAsync(VaultsTable, owner, rowKey, ValueEnvelope, new byte[] { 1, 2, 3 }, Token);
                break;
            case DataKeyAlgorithm:
                await VaultRows.SetPropertyAsync(VaultsTable, owner, rowKey, DataKeyAlgorithm, "some-other-algorithm", Token);
                break;
            default:
                await VaultRows.SetPropertyAsync(VaultsTable, owner, rowKey, WrappedDataKey, new byte[] { 9, 9, 9 }, Token);
                break;
        }
    }

    private static async Task AssertDecryptionProblemAsync(HttpResponseMessage response, PlaintextMarkers markers)
    {
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var text = await response.Content.ReadAsStringAsync(Token);
        using var body = JsonDocument.Parse(text);
        Assert.Equal(DecryptionTitle, body.RootElement.GetProperty("title").GetString());
        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.False(body.RootElement.TryGetProperty("detail", out _));
        Assert.False(body.RootElement.TryGetProperty("exception", out _));
        markers.AssertAbsentFrom(text);
        Assert.DoesNotContain(SecretDecryptionException.DefaultMessage, text, StringComparison.Ordinal);
        foreach (var reason in Enum.GetNames<DecryptionFailureReason>())
        {
            Assert.DoesNotContain(reason, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("at SharpVault", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(TamperNames))]
    public async Task RevealValue_DamagedRow_Returns500WithOnlyTheFixedTitleAndWritesNoAudit(string kind)
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        await TamperAsync(TestUsers.User.ObjectId, secret.Id, kind);
        Logs.Clear();

        var response = (await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View })).Response;

        await AssertDecryptionProblemAsync(response, markers);
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.SecretValueRevealed, AuditEventType.SecretValueCopied));
        AssertLogsDoNotContain(markers.Value, markers.Notes, markers.Name);
    }

    [Fact]
    public async Task RevealValue_EveryDamageKind_ProducesIdenticalBodies()
    {
        var api = ApiFor(TestUsers.User);
        var bodies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var kind in TamperCases.Keys)
        {
            var secret = await CreateSecretAsync(api, new PlaintextMarkers());
            await TamperAsync(TestUsers.User.ObjectId, secret.Id, kind);

            var response = (await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy })).Response;

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            bodies.Add(TraceId().Replace(await response.Content.ReadAsStringAsync(Token), string.Empty));
        }

        Assert.Single(bodies);
    }

    [Fact]
    public async Task RevealNotes_DamagedRow_Returns500WithTheFixedTitle()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        await TamperAsync(TestUsers.User.ObjectId, secret.Id, "swapped-envelopes");

        var response = (await api.RevealSecretAsync(secret.Id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.View })).Response;

        await AssertDecryptionProblemAsync(response, markers);
    }

    [Fact]
    public async Task VersionRoutes_DamagedVersion_Return500WithTheFixedTitle()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var updated = Require(await api.UpdateSecretAsync(secret.Id, secret.ConcurrencyToken, new UpdateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = markers.Name,
            ReplaceValue = true,
            Value = markers.Value + "-v2",
        }));
        var version = Assert.Single(Require(await api.ListHistoryAsync(secret.Id)).Items);
        await VaultRows.SetPropertyAsync(VaultsTable, TestUsers.User.ObjectId, VaultKeys.VersionRowKey(secret.Id, version.VersionId), ValueEnvelope, new byte[] { 1, 2, 3 }, Token);
        Logs.Clear();

        var reveal = (await api.RevealVersionAsync(secret.Id, version.VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response;
        var restore = (await api.RestoreVersionAsync(secret.Id, version.VersionId, updated.ConcurrencyToken)).Response;

        await AssertDecryptionProblemAsync(reveal, markers);
        await AssertDecryptionProblemAsync(restore, markers);
        Assert.Empty(await VaultAuditRowsAsync(AuditEventType.SecretVersionRevealed, AuditEventType.SecretVersionRestored));
        Assert.Equal(updated.Id, Require(await api.GetSecretAsync(secret.Id)).Id);
        AssertLogsDoNotContain(markers.Value, markers.Name);
    }

    [Fact]
    public async Task ListSecrets_DamagedRow_StillReturns200AndMarksTheItemDamaged()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        await TamperAsync(TestUsers.User.ObjectId, secret.Id, "bad-wrapped-key");

        var list = await api.ListSecretsAsync();

        Assert.Equal(HttpStatusCode.OK, list.Response.StatusCode);
        Assert.NotNull(list.Body);
        Assert.Contains(list.Body.Items, item => item.Id == secret.Id);
    }

    [Fact]
    public async Task CreateSecret_Created_LocationHeaderPointsAtTheNewSecretAndResolves()
    {
        var api = ApiFor(TestUsers.User);

        var created = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "located" });

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        Assert.NotNull(created.Body);
        var location = created.Response.Headers.Location?.OriginalString;
        Assert.Equal($"/api/vault/secrets/{created.Body.Id:D}", location);
        var followed = await api.Client.GetAsync(location, Token);
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    [Fact]
    public async Task CreateFolder_Created_LocationHeaderPointsAtTheNewFolder()
    {
        var api = ApiFor(TestUsers.User);

        var created = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "located" });

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        Assert.NotNull(created.Body);
        Assert.Equal($"/api/vault/folders/{created.Body.Id:D}", created.Response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateSecretInFolder_Created_LocationHeaderIsTheSecretAndNotTheFolder()
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "parent" }));

        var created = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "child", FolderId = folder.Id });

        Assert.NotNull(created.Body);
        Assert.Equal($"/api/vault/secrets/{created.Body.Id:D}", created.Response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task OpenApi_Operations_OnlyCreateSecretAndCreateFolderDeclare201()
    {
        using var document = await LoadOpenApiAsync();

        var created = Operations(document)
            .Where(operation => operation.Statuses.Contains("201") && operation.Path.StartsWith("/api/vault/", StringComparison.Ordinal))
            .Select(operation => $"{operation.Method} {operation.Path}")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["POST /api/vault/folders", "POST /api/vault/secrets"], created);
    }

    [Fact]
    public async Task OpenApi_VaultOperations_DeclareExactlyTheDocumentedStatuses()
    {
        using var document = await LoadOpenApiAsync();

        var declared = Operations(document)
            .Where(operation => operation.Path.StartsWith("/api/vault/", StringComparison.Ordinal))
            .ToDictionary(operation => $"{operation.Method} {operation.Path}", operation => operation.Statuses.Order(StringComparer.Ordinal).ToList());

        Assert.Equal(DocumentedStatuses.Count, declared.Count);
        foreach (var (key, expected) in DocumentedStatuses)
        {
            Assert.True(declared.TryGetValue(key, out var actual), $"{key} is not in the OpenAPI document.");
            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
        }
    }

    [Fact]
    public async Task VaultEndpoints_ObservedStatuses_AreAllDeclaredAndOnlyRaceOnlyStatusesAreUnobserved()
    {
        using var document = await LoadOpenApiAsync();
        var declared = Operations(document)
            .Where(operation => operation.Path.StartsWith("/api/vault/", StringComparison.Ordinal))
            .ToDictionary(operation => $"{operation.Method} {operation.Path}", operation => operation.Statuses);
        var observed = await ObserveStatusesAsync();

        var undeclared = observed
            .SelectMany(pair => pair.Value.Select(status => $"{pair.Key} {status}"))
            .Where(entry => !declared[entry[..entry.LastIndexOf(' ')]].Contains(entry[(entry.LastIndexOf(' ') + 1)..]))
            .ToList();
        var unobserved = declared
            .SelectMany(pair => pair.Value.Select(status => $"{pair.Key} {status}"))
            .Where(entry => !observed.TryGetValue(entry[..entry.LastIndexOf(' ')], out var seen) || !seen.Contains(entry[(entry.LastIndexOf(' ') + 1)..]))
            .Where(entry => !entry.EndsWith(" 429", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Empty(undeclared);
        Assert.Equal(UnobservableDeclarations.Order(StringComparer.Ordinal), unobserved);
    }

    private async Task<Dictionary<string, HashSet<string>>> ObserveStatusesAsync()
    {
        var observed = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var owner = ApiFor(TestUsers.User);
        var anonymous = new VaultTestApi(CreateUnauthenticatedClient());
        var roleless = ApiFor(TestUsers.AuthenticatedWithoutRole);
        var markers = new PlaintextMarkers();
        var folder = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = markers.Folder }));
        var secret = await CreateSecretAsync(owner, markers, folder.Id);
        var staleToken = secret.ConcurrencyToken;
        var updated = Require(await owner.UpdateSecretAsync(secret.Id, staleToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = markers.Name, ReplaceValue = true, Value = "v2" }));
        var version = Assert.Single(Require(await owner.ListHistoryAsync(secret.Id)).Items);
        var unknown = Guid.NewGuid();
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };
        var versionReveal = new RevealVersionRequestDto { Intent = RevealIntentDto.View };
        var validUpdate = new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "x" };

        async Task<string> Fresh() => Require(await owner.GetSecretAsync(secret.Id)).ConcurrencyToken;
        async Task<Guid> BinnedSecret()
        {
            var doomed = await CreateSecretAsync(owner, new PlaintextMarkers());
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteSecretAsync(doomed.Id, doomed.ConcurrencyToken)).StatusCode);
            return doomed.Id;
        }

        async Task<Guid> BinnedFolder()
        {
            var doomed = Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "binned" }));
            Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteFolderAsync(doomed.Id, FolderDeleteModeDto.DeleteContents)).StatusCode);
            return doomed.Id;
        }

        var probes = new List<(string Operation, Func<VaultTestApi, Task<HttpResponseMessage>> Send)>
        {
            ("GET /api/vault/secrets", api => Status(api.ListSecretsAsync())),
            ("GET /api/vault/secrets", api => Status(api.ListSecretsAsync(pageSize: 0))),
            ("GET /api/vault/secrets/{secretId}", api => Status(api.GetSecretAsync(secret.Id))),
            ("GET /api/vault/secrets/{secretId}", api => Status(api.GetSecretAsync(unknown))),
            ("GET /api/vault/secrets/{secretId}", api => Status(api.GetSecretAsync(Guid.Empty))),
            ("POST /api/vault/secrets", api => Status(api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "ok" }))),
            ("POST /api/vault/secrets", api => Status(api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = string.Empty }))),
            ("POST /api/vault/secrets", api => Status(api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "n", FolderId = unknown }))),
            ("PUT /api/vault/secrets/{secretId}", async api => (await api.UpdateSecretAsync(secret.Id, await Fresh(), validUpdate)).Response),
            ("PUT /api/vault/secrets/{secretId}", api => Status(api.UpdateSecretAsync(secret.Id, null, validUpdate))),
            ("PUT /api/vault/secrets/{secretId}", api => Status(api.UpdateSecretAsync(unknown, updated.ConcurrencyToken, validUpdate))),
            ("PUT /api/vault/secrets/{secretId}", api => Status(api.UpdateSecretAsync(secret.Id, staleToken, validUpdate))),
            ("POST /api/vault/secrets/{secretId}/move", api => Status(api.MoveSecretAsync(secret.Id, null, new MoveSecretRequestDto()))),
            ("POST /api/vault/secrets/{secretId}/move", api => Status(api.MoveSecretAsync(unknown, updated.ConcurrencyToken, new MoveSecretRequestDto()))),
            ("POST /api/vault/secrets/{secretId}/move", api => Status(api.MoveSecretAsync(secret.Id, staleToken, new MoveSecretRequestDto()))),
            ("POST /api/vault/secrets/{secretId}/move", async api => (await api.MoveSecretAsync(secret.Id, await Fresh(), new MoveSecretRequestDto { FolderId = folder.Id })).Response),
            ("DELETE /api/vault/secrets/{secretId}", api => api.DeleteSecretAsync(secret.Id, null)),
            ("DELETE /api/vault/secrets/{secretId}", async api => { var doomed = await CreateSecretAsync(owner, new PlaintextMarkers()); return await api.DeleteSecretAsync(doomed.Id, doomed.ConcurrencyToken); }),
            ("DELETE /api/vault/secrets/{secretId}", api => api.DeleteSecretAsync(unknown, updated.ConcurrencyToken)),
            ("DELETE /api/vault/secrets/{secretId}", api => api.DeleteSecretAsync(secret.Id, staleToken)),
            ("POST /api/vault/secrets/{secretId}/reveal", api => Status(api.RevealSecretAsync(secret.Id, reveal))),
            ("POST /api/vault/secrets/{secretId}/reveal", api => Status(api.RevealSecretAsync(unknown, reveal))),
            ("POST /api/vault/secrets/{secretId}/reveal", api => api.PostRawAsync($"/api/vault/secrets/{secret.Id:D}/reveal", "{\"field\":9,\"intent\":1}")),
            ("GET /api/vault/secrets/{secretId}/history", api => Status(api.ListHistoryAsync(secret.Id))),
            ("GET /api/vault/secrets/{secretId}/history", api => Status(api.ListHistoryAsync(unknown))),
            ("GET /api/vault/secrets/{secretId}/history", api => Status(api.ListHistoryAsync(Guid.Empty))),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/reveal", api => Status(api.RevealVersionAsync(secret.Id, version.VersionId, versionReveal))),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/reveal", api => Status(api.RevealVersionAsync(secret.Id, unknown, versionReveal))),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/reveal", api => api.PostRawAsync($"/api/vault/secrets/{secret.Id:D}/history/{version.VersionId:D}/reveal", "{\"intent\":\"nope\"}")),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/restore", api => Status(api.RestoreVersionAsync(secret.Id, unknown, updated.ConcurrencyToken))),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/restore", async api => (await api.RestoreVersionAsync(secret.Id, version.VersionId, await Fresh())).Response),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/restore", api => Status(api.RestoreVersionAsync(secret.Id, version.VersionId, null))),
            ("POST /api/vault/secrets/{secretId}/history/{versionId}/restore", api => Status(api.RestoreVersionAsync(secret.Id, version.VersionId, staleToken))),
            ("GET /api/vault/folders", api => Status(api.ListFoldersAsync())),
            ("POST /api/vault/folders", api => Status(api.CreateFolderAsync(new CreateFolderRequestDto { Name = "ok" }))),
            ("POST /api/vault/folders", api => Status(api.CreateFolderAsync(new CreateFolderRequestDto { Name = string.Empty }))),
            ("POST /api/vault/folders", api => Status(api.CreateFolderAsync(new CreateFolderRequestDto { Name = "n", ParentId = unknown }))),
            ("PUT /api/vault/folders/{folderId}", api => Status(api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = "renamed" }))),
            ("PUT /api/vault/folders/{folderId}", api => Status(api.RenameFolderAsync(folder.Id, new RenameFolderRequestDto { Name = string.Empty }))),
            ("PUT /api/vault/folders/{folderId}", api => Status(api.RenameFolderAsync(unknown, new RenameFolderRequestDto { Name = "x" }))),
            ("POST /api/vault/folders/{folderId}/move", api => Status(api.MoveFolderAsync(folder.Id, new MoveFolderRequestDto()))),
            ("POST /api/vault/folders/{folderId}/move", api => Status(api.MoveFolderAsync(unknown, new MoveFolderRequestDto()))),
            ("POST /api/vault/folders/{folderId}/move", api => api.PostRawAsync($"/api/vault/folders/{folder.Id:D}/move", "{\"parentId\":\"not-a-guid\"}")),
            ("DELETE /api/vault/folders/{folderId}", api => api.DeleteFolderAsync(folder.Id, (string?)null)),
            ("DELETE /api/vault/folders/{folderId}", async api => await api.DeleteFolderAsync(Require(await owner.CreateFolderAsync(new CreateFolderRequestDto { Name = "doomed" })).Id, FolderDeleteModeDto.DeleteContents)),
            ("DELETE /api/vault/folders/{folderId}", api => api.DeleteFolderAsync(unknown, FolderDeleteModeDto.MoveToParent)),
            ("GET /api/vault/recycle-bin", api => Status(api.ListRecycleBinAsync())),
            ("GET /api/vault/recycle-bin", api => Status(api.ListRecycleBinAsync(pageSize: 0))),
            ("POST /api/vault/recycle-bin/secrets/{secretId}/restore", api => Status(api.RestoreRecycledSecretAsync(unknown))),
            ("POST /api/vault/recycle-bin/secrets/{secretId}/restore", async api => (await api.RestoreRecycledSecretAsync(await BinnedSecret())).Response),
            ("POST /api/vault/recycle-bin/secrets/{secretId}/restore", api => Status(api.RestoreRecycledSecretAsync(Guid.Empty))),
            ("POST /api/vault/recycle-bin/folders/{folderId}/restore", api => Status(api.RestoreRecycledFolderAsync(unknown))),
            ("POST /api/vault/recycle-bin/folders/{folderId}/restore", async api => (await api.RestoreRecycledFolderAsync(await BinnedFolder())).Response),
            ("POST /api/vault/recycle-bin/folders/{folderId}/restore", api => Status(api.RestoreRecycledFolderAsync(Guid.Empty))),
            ("DELETE /api/vault/recycle-bin/secrets/{secretId}", api => api.PurgeRecycledSecretAsync(unknown)),
            ("DELETE /api/vault/recycle-bin/secrets/{secretId}", async api => await api.PurgeRecycledSecretAsync(await BinnedSecret())),
            ("DELETE /api/vault/recycle-bin/secrets/{secretId}", api => api.PurgeRecycledSecretAsync(Guid.Empty)),
            ("DELETE /api/vault/recycle-bin/folders/{folderId}", api => api.PurgeRecycledFolderAsync(unknown)),
            ("DELETE /api/vault/recycle-bin/folders/{folderId}", async api => await api.PurgeRecycledFolderAsync(await BinnedFolder())),
            ("DELETE /api/vault/recycle-bin/folders/{folderId}", api => api.PurgeRecycledFolderAsync(Guid.Empty)),
        };

        foreach (var (operation, send) in probes)
        {
            await Record(observed, operation, send(owner));
            await Record(observed, operation, send(anonymous));
            await Record(observed, operation, send(roleless));
        }

        return observed;
    }

    private static async Task Record(Dictionary<string, HashSet<string>> observed, string operation, Task<HttpResponseMessage> call)
    {
        var response = await call;
        if (!observed.TryGetValue(operation, out var statuses))
        {
            statuses = [];
            observed[operation] = statuses;
        }

        statuses.Add(((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static async Task<HttpResponseMessage> Status<T>(Task<(HttpResponseMessage Response, T? Body)> call) => (await call).Response;

    private async Task<JsonDocument> LoadOpenApiAsync()
    {
        using var client = CreateUnauthenticatedClient();
        var response = await client.GetAsync("/openapi/v1.json", Token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
    }

    private static List<(string Method, string Path, HashSet<string> Statuses)> Operations(JsonDocument document)
    {
        var operations = new List<(string, string, HashSet<string>)>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                var statuses = method.Value.GetProperty("responses").EnumerateObject().Select(response => response.Name).ToHashSet(StringComparer.Ordinal);
                operations.Add((method.Name.ToUpperInvariant(), path.Name, statuses));
            }
        }

        return operations;
    }

    private static readonly Dictionary<string, string[]> DocumentedStatuses = new Dictionary<string, string[]>
    {
        ["GET /api/vault/secrets"] = ["200", "400", "401", "403", "429"],
        ["GET /api/vault/secrets/{secretId}"] = ["200", "400", "401", "403", "404", "429"],
        ["POST /api/vault/secrets"] = ["201", "400", "401", "403", "404", "409", "429"],
        ["PUT /api/vault/secrets/{secretId}"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["POST /api/vault/secrets/{secretId}/move"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["DELETE /api/vault/secrets/{secretId}"] = ["204", "400", "401", "403", "404", "409", "429"],
        ["POST /api/vault/secrets/{secretId}/reveal"] = ["200", "400", "401", "403", "404", "429", "500"],
        ["GET /api/vault/secrets/{secretId}/history"] = ["200", "400", "401", "403", "404", "429"],
        ["POST /api/vault/secrets/{secretId}/history/{versionId}/reveal"] = ["200", "400", "401", "403", "404", "429", "500"],
        ["POST /api/vault/secrets/{secretId}/history/{versionId}/restore"] = ["200", "400", "401", "403", "404", "409", "429", "500"],
        ["GET /api/vault/folders"] = ["200", "400", "401", "403", "429"],
        ["POST /api/vault/folders"] = ["201", "400", "401", "403", "404", "409", "429"],
        ["PUT /api/vault/folders/{folderId}"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["POST /api/vault/folders/{folderId}/move"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["DELETE /api/vault/folders/{folderId}"] = ["204", "400", "401", "403", "404", "409", "429"],
        ["GET /api/vault/recycle-bin"] = ["200", "400", "401", "403", "429"],
        ["POST /api/vault/recycle-bin/secrets/{secretId}/restore"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["POST /api/vault/recycle-bin/folders/{folderId}/restore"] = ["200", "400", "401", "403", "404", "409", "429"],
        ["DELETE /api/vault/recycle-bin/secrets/{secretId}"] = ["204", "400", "401", "403", "404", "409", "429"],
        ["DELETE /api/vault/recycle-bin/folders/{folderId}"] = ["204", "400", "401", "403", "404", "409", "429"],
    };

    private static readonly string[] UnobservableDeclarations =
    [
        "GET /api/vault/folders 400",
        "POST /api/vault/secrets 409",
        "POST /api/vault/folders 409",
        "PUT /api/vault/folders/{folderId} 409",
        "POST /api/vault/folders/{folderId}/move 409",
        "DELETE /api/vault/folders/{folderId} 409",
        "POST /api/vault/recycle-bin/secrets/{secretId}/restore 409",
        "POST /api/vault/recycle-bin/folders/{folderId}/restore 409",
        "DELETE /api/vault/recycle-bin/secrets/{secretId} 409",
        "DELETE /api/vault/recycle-bin/folders/{folderId} 409",
        "POST /api/vault/secrets/{secretId}/reveal 500",
        "POST /api/vault/secrets/{secretId}/history/{versionId}/reveal 500",
        "POST /api/vault/secrets/{secretId}/history/{versionId}/restore 500",
    ];
}
