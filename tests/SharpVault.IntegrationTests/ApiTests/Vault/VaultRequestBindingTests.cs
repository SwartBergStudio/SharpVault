// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultRequestBindingTests(ApiTestFactory factory) : VaultTestBase(factory)
{
    private const string StaleLookingETag = "W/\"datetime'2026-01-01T00%3A00%3A00.0000000Z'\"";

    public static TheoryData<string> BadModes() =>
    [
        "1",
        "movetoparent",
        "MoveToParent,DeleteContents",
        " MoveToParent",
        "Unknown",
        "SV-MODE-ECHO-MARKER",
        string.Empty,
    ];

    public static TheoryData<string, string> UnknownMemberRoutes() => new()
    {
        { ApiRoutes.VaultSecrets, "POST" },
        { "/api/vault/secrets/{0}", "PUT" },
        { "/api/vault/secrets/{0}/reveal", "POST" },
        { ApiRoutes.VaultFolders, "POST" },
    };

    public static TheoryData<string> BadPageSizes() => ["abc", "0", "201", "99999999999"];

    private static string ValidBodyFor(string route) => route switch
    {
        _ when route.EndsWith("/reveal", StringComparison.Ordinal) && route.Contains("/history/", StringComparison.Ordinal) => "{\"intent\":\"View\"}",
        _ when route.EndsWith("/reveal", StringComparison.Ordinal) => "{\"field\":\"Value\",\"intent\":\"View\"}",
        _ when route.EndsWith("/move", StringComparison.Ordinal) => "{}",
        _ when route.StartsWith("/api/vault/folders", StringComparison.Ordinal) => "{\"name\":\"n\"}",
        _ => "{\"type\":\"Login\",\"name\":\"n\"}",
    };

    private static string WithExtraMember(string json, string member) =>
        json.TrimEnd('}') + (json == "{}" ? string.Empty : ",") + $"\"{member}\":\"x\"}}";

    [Fact]
    public async Task CreateSecret_ValidBody_Returns201()
    {
        var api = ApiFor(TestUsers.User);

        var created = await api.CreateSecretAsync(new CreateSecretRequestDto { Type = SecretTypeDto.Login, Name = "valid" });

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
    }

    [Fact]
    public async Task ListSecrets_Anonymous_Returns401()
    {
        var api = new VaultTestApi(CreateUnauthenticatedClient());

        Assert.Equal(HttpStatusCode.Unauthorized, (await api.ListSecretsAsync()).Response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BadModes))]
    public async Task DeleteFolder_InvalidMode_Returns400AndDeletesNothing(string mode)
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "keep" }));
        ResetCounters();

        var response = await api.DeleteFolderAsync(folder.Id, mode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("SV-MODE-ECHO-MARKER", await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Empty(StorageRequests.VaultWrites());
        Assert.Single(Require(await api.ListFoldersAsync()).Items);
    }

    [Fact]
    public async Task DeleteFolder_MissingMode_Returns400AndDeletesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "keep" }));
        ResetCounters();

        var response = await api.DeleteFolderAsync(folder.Id, (string?)null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StorageRequests.VaultWrites());
        Assert.Single(Require(await api.ListFoldersAsync()).Items);
    }

    [Theory]
    [InlineData("MoveToParent")]
    [InlineData("DeleteContents")]
    public async Task DeleteFolder_UnknownFolder_Returns404ForEveryMode(string mode)
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.DeleteFolderAsync(Guid.NewGuid(), mode);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(UnknownMemberRoutes))]
    public async Task InputRoutes_UnknownJsonMember_Returns400BeforeAnyLookup(string routeTemplate, string method)
    {
        var api = ApiFor(TestUsers.User);
        var path = routeTemplate.Replace("{0}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        var marker = "SV-MEMBER-" + Guid.NewGuid().ToString("N");
        ResetCounters();

        var response = await api.SendRawAsync(new HttpMethod(method), path, WithExtraMember(ValidBodyFor(path), marker), StaleLookingETag);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(marker, await response.Content.ReadAsStringAsync(Token), StringComparison.Ordinal);
        Assert.Empty(StorageRequests.VaultRequests());
    }

    [Theory]
    [MemberData(nameof(UnknownMemberRoutes))]
    public async Task InputRoutes_ServerControlledMember_Returns400AndWritesNothing(string routeTemplate, string method)
    {
        var api = ApiFor(TestUsers.User);
        var path = routeTemplate.Replace("{0}", Guid.NewGuid().ToString("D"), StringComparison.Ordinal);
        ResetCounters();

        var response = await api.SendRawAsync(new HttpMethod(method), path, WithExtraMember(ValidBodyFor(path), "ownerId"), StaleLookingETag);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StorageRequests.VaultWrites());
    }

    [Theory]
    [InlineData("{\"type\":1,\"name\":\"n\"}")]
    [InlineData("{\"type\":\"Nope\",\"name\":\"n\"}")]
    [InlineData("{\"type\":\"Login\",\"name\":\"\\ud800\"}")]
    [InlineData("{\"type\":\"Login\",\"name\":\"n\",\"tags\":\"a\"}")]
    [InlineData("{\"type\":\"Login\",\"name\":\"n\",\"folderId\":\"not-a-guid\"}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    public async Task CreateSecret_MalformedBody_Returns400AndWritesNothing(string json)
    {
        var api = ApiFor(TestUsers.User);
        ResetCounters();

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StorageRequests.VaultWrites());
        Assert.Equal(0, Factory.KeyWraps.Wraps);
    }

    [Fact]
    public async Task CreateSecret_NoContentTypeBody_IsRejectedWithoutServerError()
    {
        var api = ApiFor(TestUsers.User);
        using var request = new HttpRequestMessage(HttpMethod.Post, ApiRoutes.VaultSecrets)
        {
            Content = new StringContent("{\"type\":\"Login\",\"name\":\"n\"}", Encoding.UTF8, "text/plain"),
        };

        var response = await api.Client.SendAsync(request, Token);

        Assert.True(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType, $"Unexpected status {response.StatusCode}.");
    }

    [Theory]
    [MemberData(nameof(BadPageSizes))]
    public async Task ListSecrets_BadPageSize_Returns400(string pageSize)
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.VaultSecrets}?{ApiQuery.PageSize}={pageSize}", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BadPageSizes))]
    public async Task ListRecycleBin_BadPageSize_Returns400(string pageSize)
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.RecycleBin}?{ApiQuery.PageSize}={pageSize}", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("unfiled=abc")]
    [InlineData("folderId=not-a-guid")]
    [InlineData("folderId=00000000-0000-0000-0000-000000000000")]
    [InlineData("continuationToken=%27%20or%20PartitionKey%20ne%20%27")]
    [InlineData("search=%20%20")]
    public async Task ListSecrets_BadQuery_Returns400AndReadsNothing(string query)
    {
        var api = ApiFor(TestUsers.User);
        ResetCounters();

        var response = await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.VaultSecrets}?{query}", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(StorageRequests.VaultRequests());
    }

    [Fact]
    public async Task MutatingSecretRoutes_MissingIfMatch_Return400AndWriteNothing()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        ResetCounters();

        var update = await api.UpdateSecretAsync(secret.Id, null, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "renamed" });
        var move = await api.MoveSecretAsync(secret.Id, null, new MoveSecretRequestDto());
        var delete = await api.DeleteSecretAsync(secret.Id, null);
        var restore = await api.RestoreVersionAsync(secret.Id, Guid.NewGuid(), null);

        Assert.Equal(HttpStatusCode.BadRequest, update.Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, move.Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, restore.Response.StatusCode);
        Assert.Empty(StorageRequests.VaultWrites());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("W/\"datetime'x y'\"")]
    public async Task MutatingSecretRoutes_MalformedIfMatch_Return400AndWriteNothing(string ifMatch)
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        ResetCounters();

        var delete = await api.DeleteSecretAsync(secret.Id, ifMatch);

        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Empty(StorageRequests.VaultWrites());
        Assert.Equal(HttpStatusCode.OK, (await api.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Fact]
    public async Task ItemRoutes_UnknownId_Return404WithIdenticalBodies()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        var unknown = Guid.NewGuid();
        var reveal = new RevealSecretRequestDto { Field = RevealFieldDto.Value, Intent = RevealIntentDto.View };

        var responses = new List<HttpResponseMessage>
        {
            (await api.GetSecretAsync(unknown)).Response,
            (await api.UpdateSecretAsync(unknown, secret.ConcurrencyToken, new UpdateSecretRequestDto { Type = SecretTypeDto.Login, Name = "x" })).Response,
            (await api.MoveSecretAsync(unknown, secret.ConcurrencyToken, new MoveSecretRequestDto())).Response,
            await api.DeleteSecretAsync(unknown, secret.ConcurrencyToken),
            (await api.RevealSecretAsync(unknown, reveal)).Response,
            (await api.ListHistoryAsync(unknown)).Response,
            (await api.RevealVersionAsync(secret.Id, unknown, new RevealVersionRequestDto { Intent = RevealIntentDto.View })).Response,
            (await api.RestoreVersionAsync(secret.Id, unknown, secret.ConcurrencyToken)).Response,
            (await api.RenameFolderAsync(unknown, new RenameFolderRequestDto { Name = "x" })).Response,
            (await api.MoveFolderAsync(unknown, new MoveFolderRequestDto())).Response,
            await api.DeleteFolderAsync(unknown, FolderDeleteModeDto.MoveToParent),
            (await api.RestoreRecycledSecretAsync(unknown)).Response,
            (await api.RestoreRecycledFolderAsync(unknown)).Response,
            await api.PurgeRecycledSecretAsync(unknown),
            await api.PurgeRecycledFolderAsync(unknown),
        };

        var bodies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            bodies.Add(StripTraceId(await response.Content.ReadAsStringAsync(Token)));
        }

        var body = Assert.Single(bodies);
        Assert.Contains("The item was not found.", body, StringComparison.Ordinal);
        Assert.DoesNotContain(unknown.ToString("D"), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ItemRoutes_NullBodyOnUpdate_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());

        var response = await api.PutRawAsync(ApiRoutes.VaultSecret.Replace("{secretId:guid}", secret.Id.ToString("D"), StringComparison.Ordinal), "null", secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.GetSecretAsync(secret.Id)).Response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/vault/secrets/not-a-guid")]
    [InlineData("PUT", "/api/vault/folders/123")]
    [InlineData("DELETE", "/api/vault/recycle-bin/secrets/xyz")]
    public async Task Routes_NonGuidId_Return404WithoutReachingTheVault(string method, string path)
    {
        var api = ApiFor(TestUsers.User);
        ResetCounters();

        var response = await api.SendRawAsync(new HttpMethod(method), path, method == "POST" ? "{}" : null, null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(StorageRequests.VaultRequests());
    }

    private static string StripTraceId(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, "\"traceId\":\"[^\"]*\"", "\"traceId\":\"\"", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}
