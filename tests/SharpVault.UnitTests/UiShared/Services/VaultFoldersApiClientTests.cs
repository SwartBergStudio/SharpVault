// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class VaultFoldersApiClientTests : IDisposable
{
    private const string FolderJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","parentId":null,"name":"Travel","createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","isDamaged":false}""";

    private readonly ApiClientHarness _harness = new();
    private readonly VaultFoldersApiClient _client;

    public VaultFoldersApiClientTests()
    {
        _client = _harness.Create<VaultFoldersApiClient>((http, tokens, auth, session, logger) => new VaultFoldersApiClient(http, tokens, auth, session, logger));
        _harness.Handler.Respond(HttpStatusCode.OK, FolderJson);
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task ListFolders_GetsTheFolderCollection()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        var response = await _client.ListFoldersAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal("/api/vault/folders", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task CreateFolder_PostsNameAndParent()
    {
        var parentId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.Created, FolderJson);

        await _client.CreateFolderAsync(new CreateFolderRequestDto { Name = "Travel", ParentId = parentId }, TestContext.Current.CancellationToken);

        Assert.Equal($$"""{"name":"Travel","parentId":"{{parentId:D}}"}""", Only.Body);
    }

    [Fact]
    public async Task RenameFolder_PutsToTheBoundRoute()
    {
        var id = Guid.NewGuid();

        await _client.RenameFolderAsync(id, new RenameFolderRequestDto { Name = "Trips" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal($"/api/vault/folders/{id:D}", Only.Uri.AbsolutePath);
        Assert.Equal("""{"name":"Trips"}""", Only.Body);
    }

    [Fact]
    public async Task MoveFolder_NullParent_PostsNull()
    {
        var id = Guid.NewGuid();

        await _client.MoveFolderAsync(id, new MoveFolderRequestDto { ParentId = null }, TestContext.Current.CancellationToken);

        Assert.Equal($"/api/vault/folders/{id:D}/move", Only.Uri.AbsolutePath);
        Assert.Equal("""{"parentId":null}""", Only.Body);
    }

    [Theory]
    [InlineData(FolderDeleteModeDto.MoveToParent, "MoveToParent")]
    [InlineData(FolderDeleteModeDto.DeleteContents, "DeleteContents")]
    public async Task DeleteFolder_SendsModeAsExactName(FolderDeleteModeDto mode, string expected)
    {
        var id = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await _client.DeleteFolderAsync(id, mode, TestContext.Current.CancellationToken);

        Assert.True(response.Value);
        Assert.Equal(HttpMethod.Delete, Only.Method);
        Assert.Equal($"/api/vault/folders/{id:D}", Only.Uri.AbsolutePath);
        Assert.Equal($"?mode={expected}", Only.Uri.Query);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public async Task DeleteFolder_UndefinedMode_ThrowsBeforeAnyCall(int mode)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _client.DeleteFolderAsync(Guid.NewGuid(), (FolderDeleteModeDto)mode, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }
}
