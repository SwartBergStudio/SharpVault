// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultMassAssignmentTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private static readonly Guid Owner = TestUsers.User.ObjectId;

    public static TheoryData<string> ServerControlledMembers() =>
        ["ownerId", "id", "revision", "createdAt", "dataKey", "deletedAt"];

    public static TheoryData<string> MalformedBodies() =>
    [
        "{\"type\":\"Login\",\"name\":\"n\",\"unknown\":1}",
        "{\"type\":1,\"name\":\"n\"}",
        "{\"type\":\"login\",\"name\":\"n\"}",
        "{\"type\":\"Login,Card\",\"name\":\"n\"}",
        "{\"type\":\"Nope\",\"name\":\"n\"}",
        "{\"type\":\"Login\",\"name\":\"\\ud800\"}",
        "null",
        string.Empty,
        "{",
    ];

    public static TheoryData<string> BadModes() => ["1", "2", "1,2", "movetoparent"];

    [Fact]
    public async Task CreateSecret_BodyWithOwnerId_Returns400AndCreatesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var json = $"{{\"type\":\"Login\",\"name\":\"n\",\"value\":\"v\",\"ownerId\":\"{TestUsers.OtherUser.ObjectId:D}\"}}";

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
        Assert.Empty(await RowsOfAsync(TestUsers.OtherUser.ObjectId));
    }

    [Fact]
    public async Task CreateSecret_BodyWithId_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var json = $"{{\"type\":\"Login\",\"name\":\"n\",\"id\":\"{Guid.NewGuid():D}\"}}";

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Theory]
    [MemberData(nameof(ServerControlledMembers))]
    public async Task UpdateSecret_BodyWithUnknownMembers_Returns400AndChangesNothing(string member)
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var before = await RowsOfAsync(Owner);
        var json = $"{{\"type\":\"Login\",\"name\":\"{markers.Name}-changed\",\"{member}\":\"{Guid.NewGuid():D}\"}}";

        var response = await api.PutRawAsync(VaultTestApi.Bind(ApiRoutes.VaultSecret, ("secretId", secret.Id)), json, secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
        Assert.Equal(markers.Name, (await api.GetSecretAsync(secret.Id)).Body!.Name);
    }

    [Fact]
    public async Task CreateFolder_BodyWithOwnerId_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var json = $"{{\"name\":\"n\",\"ownerId\":\"{TestUsers.OtherUser.ObjectId:D}\"}}";

        var response = await api.PostRawAsync(ApiRoutes.VaultFolders, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
        Assert.Empty(await RowsOfAsync(TestUsers.OtherUser.ObjectId));
    }

    [Fact]
    public async Task RevealSecret_BodyWithOwnerId_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        var before = await TotalAuditRowsAsync();
        var json = $"{{\"field\":\"Value\",\"intent\":\"View\",\"ownerId\":\"{TestUsers.OtherUser.ObjectId:D}\"}}";

        var response = await api.PostRawAsync(VaultTestApi.Bind(ApiRoutes.VaultSecretReveal, ("secretId", secret.Id)), json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await TotalAuditRowsAsync());
    }

    [Fact]
    public async Task CreateSecret_NullNameOrNullTag_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var bodies = new[]
        {
            "{\"type\":\"Login\",\"name\":null}",
            "{\"type\":\"Login\",\"name\":\"n\",\"tags\":[null]}",
            "{\"type\":\"Login\",\"name\":\"n\",\"tags\":[\"a\",null]}",
        };

        foreach (var json in bodies)
        {
            var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{json} returned {response.StatusCode}.");
        }

        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task UpdateSecret_NullName_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var secret = await CreateSecretAsync(api, new PlaintextMarkers());
        var before = await RowsOfAsync(Owner);

        var response = await api.PutRawAsync(
            VaultTestApi.Bind(ApiRoutes.VaultSecret, ("secretId", secret.Id)),
            "{\"type\":\"Login\",\"name\":null}",
            secret.ConcurrencyToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateFolder_NullName_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.PostRawAsync(ApiRoutes.VaultFolders, "{\"name\":null}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task CreateSecret_MalformedBody_Returns400InDevelopment(string json)
    {
        var api = ApiFor(TestUsers.User);
        await WarmUpAsync(TestUsers.User);
        Logs.Clear();

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(Logs.Entries, entry => entry.Level >= LogLevel.Error);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public void ApiHost_RouteHandlerOptions_DoNotThrowOnBadRequest()
    {
        var options = Factory.Services.GetRequiredService<IOptions<RouteHandlerOptions>>().Value;

        Assert.False(options.ThrowOnBadRequest);
    }

    [Theory]
    [MemberData(nameof(BadModes))]
    public async Task DeleteFolder_NumericCommaOrLowercaseMode_Returns400AndChangesNothing(string mode)
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "keep" }));
        await CreateSecretAsync(api, new PlaintextMarkers(), folder.Id);
        var before = await RowsOfAsync(Owner);

        var response = await api.DeleteFolderAsync(folder.Id, mode);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task DeleteFolder_MissingMode_Returns400AndNothingChanges()
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "keep" }));
        await CreateSecretAsync(api, new PlaintextMarkers(), folder.Id);
        var before = await RowsOfAsync(Owner);

        var response = await api.DeleteFolderAsync(folder.Id, mode: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await RowsOfAsync(Owner));
    }
}
