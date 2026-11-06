// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text.Json;
using SharpVault.Dtos.Sharing;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class ShareGrantsApiClientTests : IDisposable
{
    private const string ETag = "W/\"0x8DC1A2B3C4D5E6F\"";

    private readonly ApiClientHarness _harness = new();
    private readonly ShareGrantsApiClient _client;

    public ShareGrantsApiClientTests()
    {
        _client = _harness.Create<ShareGrantsApiClient>((http, tokens, auth, session, logger) => new ShareGrantsApiClient(http, tokens, auth, session, logger));
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    private static string GrantJson(Guid recipientId, SharePermissionDto permission = SharePermissionDto.View) =>
        JsonSerializer.Serialize(new ShareGrantDto { RecipientId = recipientId, RecipientDisplayName = "Bob", Permission = permission, ConcurrencyToken = "tok", CanChange = true }, JsonSerializerOptions.Web);

    [Fact]
    public async Task ListSecretGrants_GetsTheSecretGrantsRoute()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, JsonSerializer.Serialize(new ShareGrantListDto { Items = [new ShareGrantDto { RecipientId = recipientId, Permission = SharePermissionDto.Manage }] }, JsonSerializerOptions.Web));

        var response = await _client.ListSecretGrantsAsync(ownerId, secretId, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Get, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/secrets/{secretId:D}/grants", Only.Uri.AbsolutePath);
        var grant = Assert.Single(response.Value!.Items);
        Assert.Equal(recipientId, grant.RecipientId);
        Assert.Equal(SharePermissionDto.Manage, grant.Permission);
    }

    [Fact]
    public async Task ListFolderGrants_GetsTheFolderGrantsRoute()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        var response = await _client.ListFolderGrantsAsync(ownerId, folderId, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Get, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/folders/{folderId:D}/grants", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task CreateSecretGrant_PostsRecipientAndPermissionWithoutIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.Created, GrantJson(recipientId, SharePermissionDto.Edit));

        var response = await _client.CreateSecretGrantAsync(ownerId, secretId, new CreateShareGrantRequestDto { RecipientId = recipientId, Permission = SharePermissionDto.Edit }, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/secrets/{secretId:D}/grants", Only.Uri.AbsolutePath);
        Assert.Null(Only.IfMatch);
        Assert.Equal($$"""{"recipientId":"{{recipientId:D}}","permission":"Edit"}""", Only.Body);
    }

    [Fact]
    public async Task CreateFolderGrant_PostsToTheFolderGrantsRoute()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.Created, GrantJson(recipientId));

        await _client.CreateFolderGrantAsync(ownerId, folderId, new CreateShareGrantRequestDto { RecipientId = recipientId, Permission = SharePermissionDto.View }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/folders/{folderId:D}/grants", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task CreateGrant_Conflict_MapsToConflict()
    {
        _harness.Handler.Respond(HttpStatusCode.Conflict, string.Empty);

        var response = await _client.CreateSecretGrantAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateShareGrantRequestDto { RecipientId = Guid.NewGuid(), Permission = SharePermissionDto.View }, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Conflict, response.Status);
    }

    [Fact]
    public async Task CreateGrant_BadRequest_CarriesTheProblemDetail()
    {
        _harness.Handler.Respond(HttpStatusCode.BadRequest, """{"detail":"Recipient unavailable"}""", "application/problem+json");

        var response = await _client.CreateFolderGrantAsync(Guid.NewGuid(), Guid.NewGuid(), new CreateShareGrantRequestDto { RecipientId = Guid.NewGuid(), Permission = SharePermissionDto.View }, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Invalid, response.Status);
        Assert.Equal("Recipient unavailable", response.Detail);
    }

    [Fact]
    public async Task ChangeSecretGrant_PutsPermissionWithTheTokenAsIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, GrantJson(recipientId, SharePermissionDto.Manage));

        var response = await _client.ChangeSecretGrantAsync(ownerId, secretId, recipientId, ETag, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Manage }, TestContext.Current.CancellationToken);

        Assert.Equal(SharePermissionDto.Manage, response.Value!.Permission);
        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/secrets/{secretId:D}/grants/{recipientId:D}", Only.Uri.AbsolutePath);
        Assert.Equal(ETag, Only.IfMatch);
        Assert.Equal("""{"permission":"Manage"}""", Only.Body);
    }

    [Fact]
    public async Task ChangeFolderGrant_PutsPermissionWithTheTokenAsIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, GrantJson(recipientId, SharePermissionDto.Edit));

        await _client.ChangeFolderGrantAsync(ownerId, folderId, recipientId, ETag, new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/folders/{folderId:D}/grants/{recipientId:D}", Only.Uri.AbsolutePath);
        Assert.Equal(ETag, Only.IfMatch);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task ChangeGrant_MissingToken_ThrowsAndSendsNothing(string? token)
    {
        var request = new ChangeShareGrantRequestDto { Permission = SharePermissionDto.View };

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.ChangeSecretGrantAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), token!, request, TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.ChangeFolderGrantAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), token!, request, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }

    [Fact]
    public async Task NullRequests_ThrowAndSendNothing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateSecretGrantAsync(Guid.NewGuid(), Guid.NewGuid(), null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.CreateFolderGrantAsync(Guid.NewGuid(), Guid.NewGuid(), null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ChangeSecretGrantAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ETag, null!, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _client.ChangeFolderGrantAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ETag, null!, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }

    [Fact]
    public async Task RevokeSecretGrant_Deletes204AsTrueWithoutBodyOrIfMatch()
    {
        var ownerId = Guid.NewGuid();
        var secretId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await _client.RevokeSecretGrantAsync(ownerId, secretId, recipientId, TestContext.Current.CancellationToken);

        Assert.True(response.Value);
        Assert.Equal(HttpMethod.Delete, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/secrets/{secretId:D}/grants/{recipientId:D}", Only.Uri.AbsolutePath);
        Assert.Null(Only.IfMatch);
        Assert.Null(Only.Body);
    }

    [Fact]
    public async Task RevokeFolderGrant_DeletesTheFolderGrantRoute()
    {
        var ownerId = Guid.NewGuid();
        var folderId = Guid.NewGuid();
        var recipientId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        await _client.RevokeFolderGrantAsync(ownerId, folderId, recipientId, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Delete, Only.Method);
        Assert.Equal($"/api/sharing/owners/{ownerId:D}/folders/{folderId:D}/grants/{recipientId:D}", Only.Uri.AbsolutePath);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiCallStatus.SignInRequired)]
    [InlineData(HttpStatusCode.Forbidden, ApiCallStatus.AccessDenied)]
    [InlineData(HttpStatusCode.NotFound, ApiCallStatus.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ApiCallStatus.Conflict)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiCallStatus.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, ApiCallStatus.Failed)]
    public async Task Revoke_ErrorStatuses_MapToTheCallStatus(HttpStatusCode status, ApiCallStatus expected)
    {
        _harness.Handler.Respond(status, string.Empty);

        var response = await _client.RevokeSecretGrantAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Status);
        Assert.False(response.Value);
    }

    [Fact]
    public async Task ListGrants_TokenIsSentAsBearerOnly()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        await _client.ListSecretGrantsAsync(Guid.NewGuid(), Guid.NewGuid(), TestContext.Current.CancellationToken);

        Assert.Equal("Bearer", Only.AuthorizationScheme);
        Assert.Equal(ApiClientHarness.Token, Only.AuthorizationParameter);
    }
}
