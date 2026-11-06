// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos.Vault;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class VaultSecretsApiClientTests : IDisposable
{
    private const string ETag = "W/\"0x8DC1A2B3C4D5E6F\"";
    private const string SecretJson = """{"id":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","type":"Login","name":"Mail","username":"ada","url":null,"tags":["a"],"folderId":null,"hasValue":true,"hasNotes":false,"createdAt":"2026-01-01T00:00:00+00:00","modifiedAt":"2026-01-01T00:00:00+00:00","concurrencyToken":"tok","isDamaged":false}""";
    private const string EmptyPageJson = """{"items":[],"continuationToken":null}""";

    private readonly ApiClientHarness _harness = new();
    private readonly VaultSecretsApiClient _client;

    public VaultSecretsApiClientTests()
    {
        _client = _harness.Create<VaultSecretsApiClient>((http, tokens, auth, session, logger) => new VaultSecretsApiClient(http, tokens, auth, session, logger));
        _harness.Handler.Respond(HttpStatusCode.OK, SecretJson);
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task ListSecrets_BuildsEscapedQuery()
    {
        var folderId = Guid.Parse("3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b");
        _harness.Handler.Respond(HttpStatusCode.OK, EmptyPageJson);

        var response = await _client.ListSecretsAsync(folderId, false, "a b&c=d#e/é", 25, "tok en+/=", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(
            $"?folderId={folderId:D}&search={Uri.EscapeDataString("a b&c=d#e/é")}&pageSize=25&continuationToken={Uri.EscapeDataString("tok en+/=")}",
            Only.Uri.Query);
        Assert.Equal("/api/vault/secrets", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task ListSecrets_EmptyOptionalValues_AreLeftOut()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, EmptyPageJson);

        await _client.ListSecretsAsync(null, false, "  ", 50, string.Empty, TestContext.Current.CancellationToken);

        Assert.Equal("?pageSize=50", Only.Uri.Query);
    }

    [Fact]
    public async Task ListSecrets_Unfiled_SendsTheFlag()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, EmptyPageJson);

        await _client.ListSecretsAsync(null, true, null, 50, null, TestContext.Current.CancellationToken);

        Assert.Equal("?unfiled=true&pageSize=50", Only.Uri.Query);
    }

    [Fact]
    public async Task GetSecret_BindsTheIdIntoTheRoute()
    {
        var id = Guid.NewGuid();

        var response = await _client.GetSecretAsync(id, TestContext.Current.CancellationToken);

        Assert.Equal("Mail", response.Value!.Name);
        Assert.Equal($"/api/vault/secrets/{id:D}", Only.Uri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, Only.Method);
    }

    [Fact]
    public async Task CreateSecret_PostsTheBodyWithoutIfMatch()
    {
        _harness.Handler.Respond(HttpStatusCode.Created, SecretJson);

        await _client.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.SecureNote, Name = "n" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Post, Only.Method);
        Assert.Null(Only.IfMatch);
        Assert.Contains("\"type\":\"SecureNote\"", Only.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RevealSecret_PostsFieldAndIntentAsNames()
    {
        var id = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, """{"value":"hunter2"}""");

        var response = await _client.RevealSecretAsync(id, new RevealSecretRequestDto { Field = RevealFieldDto.Notes, Intent = RevealIntentDto.Copy }, TestContext.Current.CancellationToken);

        Assert.Equal("hunter2", response.Value!.Value);
        Assert.Equal($"/api/vault/secrets/{id:D}/reveal", Only.Uri.AbsolutePath);
        Assert.Equal("""{"field":"Notes","intent":"Copy"}""", Only.Body);
    }

    [Fact]
    public async Task UpdateSecret_SendsIfMatchHeader()
    {
        var id = Guid.NewGuid();

        await _client.UpdateSecretAsync(id, ETag, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "n" }, TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Put, Only.Method);
        Assert.Equal(ETag, Only.IfMatch);
        Assert.Equal($"/api/vault/secrets/{id:D}", Only.Uri.AbsolutePath);
    }

    [Fact]
    public async Task MoveSecret_PostsNullFolderAndIfMatch()
    {
        var id = Guid.NewGuid();

        await _client.MoveSecretAsync(id, ETag, new MoveSecretRequestDto { FolderId = null }, TestContext.Current.CancellationToken);

        Assert.Equal($"/api/vault/secrets/{id:D}/move", Only.Uri.AbsolutePath);
        Assert.Equal("""{"folderId":null}""", Only.Body);
        Assert.Equal(ETag, Only.IfMatch);
    }

    [Fact]
    public async Task DeleteSecret_204_ReturnsSuccessTrue()
    {
        _harness.Handler.Respond(HttpStatusCode.NoContent, string.Empty);

        var response = await _client.DeleteSecretAsync(Guid.NewGuid(), ETag, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.True(response.Value);
        Assert.Equal(HttpMethod.Delete, Only.Method);
        Assert.Equal(ETag, Only.IfMatch);
    }

    [Fact]
    public async Task GuardedMethods_BlankConcurrencyToken_ThrowBeforeAnyCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.UpdateSecretAsync(Guid.NewGuid(), " ", new UpdateSecretRequestDto(), cancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.MoveSecretAsync(Guid.NewGuid(), string.Empty, new MoveSecretRequestDto(), cancellationToken));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.DeleteSecretAsync(Guid.NewGuid(), " ", cancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }

    [Fact]
    public async Task EveryMethod_SendsTheBearerToken()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[],"continuationToken":null,"value":"v"}""");

        await _client.ListSecretsAsync(null, false, null, 50, null, cancellationToken);
        await _client.RevealSecretAsync(Guid.NewGuid(), new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View }, cancellationToken);

        Assert.All(_harness.Handler.Requests, request => Assert.Equal(ApiClientHarness.Token, request.AuthorizationParameter));
    }
}
