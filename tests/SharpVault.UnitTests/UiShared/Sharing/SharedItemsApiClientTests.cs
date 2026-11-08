// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Dtos.Sharing;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class SharedItemsApiClientTests : IDisposable
{
    private const string ETag = "W/\"0x8DC1A2B3C4D5E6F\"";
    private const string EmptyPageJson = """{"items":[],"continuationToken":null}""";
    private const string SecretJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","type":"Login","name":"n","tags":[],"createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","concurrencyToken":"t"}""";

    private readonly ApiClientHarness _harness = new();
    private readonly SharedItemsApiClient _client;

    public SharedItemsApiClientTests()
    {
        _client = _harness.Create<SharedItemsApiClient>((http, tokens, auth, session, logger) => new SharedItemsApiClient(http, tokens, auth, session, logger));
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task List_NoFilter_SendsOnlyThePageSize()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, EmptyPageJson);

        var response = await _client.ListAsync(null, 25, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Get, Only.Method);
        Assert.Equal("/api/shared/items", Only.Uri.AbsolutePath);
        Assert.Equal("?pageSize=25", Only.Uri.Query);
    }

    [Fact]
    public async Task List_FilterAndEscapedToken_AreInTheQuery()
    {
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, EmptyPageJson);

        await _client.ListAsync(folderId, 10, "a b&c=d#e/é+", TestContext.Current.CancellationToken);

        Assert.Equal($"?filedFolderId={folderId:D}&pageSize=10&continuationToken={Uri.EscapeDataString("a b&c=d#e/é+")}", Only.Uri.Query);
    }

    [Fact]
    public async Task GetSecret_UsesOwnerAndSecretInTheRoute()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(new SharedSecretDto { OwnerId = ownerId, Permission = SharePermissionDto.View, Item = new SecretDto { Type = SecretTypeDto.Login } }, JsonSerializerOptions.Web));

        var response = await _client.GetSecretAsync(ownerId, secretId, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Get, Only.Method);
        Assert.Equal($"/api/shared/owners/{ownerId:D}/secrets/{secretId:D}", Only.Uri.AbsolutePath);
        Assert.Equal(ApiClientHarness.Token, Only.AuthorizationParameter);
    }

    [Fact]
    public async Task UpdateSecret_SendsTokenAsIfMatchAndTheBody()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, SecretJson);

        var response = await _client.UpdateSecretAsync(ownerId, secretId, ETag, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "n" }, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal($"/api/shared/owners/{ownerId:D}/secrets/{secretId:D}", Only.Uri.AbsolutePath);
        Assert.Equal(ETag, Only.IfMatch);
        Assert.Contains("\"name\":\"n\"", Only.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSecret_BlankToken_ThrowsAndSendsNothing()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.UpdateSecretAsync(Guid.NewGuid(), Guid.NewGuid(), " ", new UpdateSecretRequestDto(), TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }

    [Fact]
    public async Task RevealSecret_PostsToTheSharedRevealRouteWithoutIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, """{"value":"v"}""");

        var response = await _client.RevealSecretAsync(ownerId, secretId, new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.Copy }, TestContext.Current.CancellationToken);

        Assert.Equal("v", response.Value!.Value);
        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Equal($"/api/shared/owners/{ownerId:D}/secrets/{secretId:D}/reveal", Only.Uri.AbsolutePath);
        Assert.Null(Only.IfMatch);
        Assert.Contains("\"field\":\"Value\"", Only.Body, StringComparison.Ordinal);
        Assert.Contains("\"intent\":\"Copy\"", Only.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFolder_BuildsRouteAndPagingQuery()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(new SharedFolderContentsDto { FolderId = folderId, OwnerId = ownerId, Permission = SharePermissionDto.View }, JsonSerializerOptions.Web));

        var response = await _client.GetFolderAsync(ownerId, folderId, 50, "t k+", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal($"/api/shared/owners/{ownerId:D}/folders/{folderId:D}", Only.Uri.AbsolutePath);
        Assert.Equal($"?pageSize=50&continuationToken={Uri.EscapeDataString("t k+")}", Only.Uri.Query);
    }

    [Fact]
    public async Task FileSecret_PutsTheFolderToTheFilingRoute()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await _client.FileSecretAsync(ownerId, secretId, new FileSharedItemRequestDto { FolderId = folderId }, TestContext.Current.CancellationToken);

        Assert.True(response.Value);
        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal($"/api/shared/owners/{ownerId:D}/secrets/{secretId:D}/filing", Only.Uri.AbsolutePath);
        Assert.Equal($$"""{"folderId":"{{folderId:D}}"}""", Only.Body);
    }

    [Fact]
    public async Task FileFolder_NullFolder_SendsExplicitNullToUnfile()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        await _client.FileFolderAsync(ownerId, folderId, new FileSharedItemRequestDto { FolderId = null }, TestContext.Current.CancellationToken);

        Assert.Equal($"/api/shared/owners/{ownerId:D}/folders/{folderId:D}/filing", Only.Uri.AbsolutePath);
        Assert.Equal("""{"folderId":null}""", Only.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, ApiCallStatus.AccessDenied)]
    [InlineData(HttpStatusCode.Conflict, ApiCallStatus.Conflict)]
    public async Task GetSecret_ErrorStatuses_MapToTheCallStatus(HttpStatusCode status, ApiCallStatus expected)
    {
        _harness.Handler.Respond(status, string.Empty);

        var response = await _client.GetSecretAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Status);
        Assert.Null(response.Value);
    }
}
