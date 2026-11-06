// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class RecycleBinApiClientTests : IDisposable
{
    private const string FolderJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","parentId":null,"name":"Travel","createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","isDamaged":false}""";
    private const string SecretJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","type":"Login","name":"n","tags":[],"createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","concurrencyToken":"t"}""";

    private readonly ApiClientHarness _harness = new();
    private readonly RecycleBinApiClient _client;

    public RecycleBinApiClientTests()
    {
        _client = _harness.Create<RecycleBinApiClient>((http, tokens, auth, session, logger) => new RecycleBinApiClient(http, tokens, auth, session, logger));
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task List_PageSizeAndEscapedToken_AreInTheQuery()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[],"continuationToken":null}""");

        await _client.ListAsync(20, "a b&c", TestContext.Current.CancellationToken);

        Assert.Equal("/api/vault/recycle-bin", Only.Uri.AbsolutePath);
        Assert.Equal($"?pageSize=20&continuationToken={Uri.EscapeDataString("a b&c")}", Only.Uri.Query);
    }

    [Fact]
    public async Task List_NoToken_LeavesItOut()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[],"continuationToken":null}""");

        await _client.ListAsync(20, null, TestContext.Current.CancellationToken);

        Assert.Equal("?pageSize=20", Only.Uri.Query);
    }

    [Fact]
    public async Task RestoreFolder_PostsWithoutBody()
    {
        var id = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, FolderJson);

        var response = await _client.RestoreFolderAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Equal($"/api/vault/recycle-bin/folders/{id:D}/restore", Only.Uri.AbsolutePath);
        Assert.Null(Only.Body);
    }

    [Fact]
    public async Task RestoreSecret_PostsToTheSecretRoute()
    {
        var id = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, SecretJson);

        await _client.RestoreSecretAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal($"/api/vault/recycle-bin/secrets/{id:D}/restore", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task Purge_204_ReturnsTrue()
    {
        var secretId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var secret = await _client.PurgeSecretAsync(secretId, TestContext.Current.CancellationToken);
        var folder = await _client.PurgeFolderAsync(folderId, TestContext.Current.CancellationToken);

        Assert.True(secret.Value);
        Assert.True(folder.Value);
        Assert.Equal(
            [$"/api/vault/recycle-bin/secrets/{secretId:D}", $"/api/vault/recycle-bin/folders/{folderId:D}"],
            _harness.Handler.Requests.Select(request => request.Uri.AbsolutePath));
        Assert.All(_harness.Handler.Requests, request => Assert.Equal(HttpMethod.Delete, request.Method));
    }
}
