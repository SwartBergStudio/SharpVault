// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class SecretHistoryApiClientTests : IDisposable
{
    private const string ETag = "W/\"0x8DC1A2B3C4D5E6F\"";
    private const string SecretJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","type":"Login","name":"Mail","username":null,"url":null,"tags":[],"folderId":null,"hasValue":true,"hasNotes":false,"createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","concurrencyToken":"tok","isDamaged":false}""";

    private readonly ApiClientHarness _harness = new();
    private readonly SecretHistoryApiClient _client;

    public SecretHistoryApiClientTests()
    {
        _client = _harness.Create<SecretHistoryApiClient>((http, tokens, auth, session, logger) => new SecretHistoryApiClient(http, tokens, auth, session, logger));
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task ListVersions_BindsTheSecretId()
    {
        var secretId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        var response = await _client.ListVersionsAsync(secretId, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal($"/api/vault/secrets/{secretId:D}/history", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task RevealVersion_BindsBothIdsAndPostsTheIntentAsName()
    {
        var secretId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, """{"value":"old"}""");

        var response = await _client.RevealVersionAsync(secretId, versionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View }, TestContext.Current.CancellationToken);

        Assert.Equal("old", response.Value!.Value);
        Assert.Equal($"/api/vault/secrets/{secretId:D}/history/{versionId:D}/reveal", Only.Uri.AbsolutePath);
        Assert.Equal("""{"intent":"View"}""", Only.Body);
    }

    [Fact]
    public async Task RestoreVersion_SendsIfMatchAndBothIds()
    {
        var secretId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, SecretJson);

        var response = await _client.RestoreVersionAsync(secretId, versionId, ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Equal($"/api/vault/secrets/{secretId:D}/history/{versionId:D}/restore", Only.Uri.AbsolutePath);
        Assert.Equal(ETag, Only.IfMatch);
        Assert.Null(Only.Body);
    }

    [Fact]
    public async Task RestoreVersion_BlankConcurrencyToken_ThrowsBeforeAnyCall()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.RestoreVersionAsync(Guid.NewGuid(), Guid.NewGuid(), " ", TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }
}
