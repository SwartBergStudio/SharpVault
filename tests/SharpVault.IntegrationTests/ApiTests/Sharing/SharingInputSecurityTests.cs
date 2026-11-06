// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Dtos;
using SharpVault.Dtos.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Sharing;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class SharingInputSecurityTests(ApiTestFactory factory) : SharingSecurityTestBase(factory)
{
    private const string ODataTerm = "' or PartitionKey ne '";

    public static TheoryData<string> RejectedSearchTerms() =>
    [
        string.Empty,
        " ",
        "a",
        "ab",
        "  ab  ",
        "***",
        "a*b",
        "abc*",
        "%%%",
        "ab\"c",
        "abc\\",
        "abc\u0000",
        "abc‮",
        "abc​",
        "ab\ncd",
        "abc\tdef",
        "<script>",
        "a;b;c",
        "abc(d)",
        "abc:d",
        "{abc}",
        "abc OR def\"",
        new string('a', 101),
    ];

    public static TheoryData<string> NonGuidRoutes() =>
    [
        "shared-secret",
        "shared-secret-reveal",
        "shared-folder",
        "secret-grants",
        "secret-grant",
        "folder-grants",
        "folder-grant",
    ];

    public static TheoryData<string> BadSegments() => ["not-a-guid", "%27%20or%201%3D1", "..%2F..", "12345", "g0000000-0000-0000-0000-000000000000"];

    public static TheoryData<string> EmptyGuidRoutes() =>
    [
        "get-secret",
        "update-secret",
        "reveal-secret",
        "get-folder",
        "file-secret",
        "list-grants",
        "create-grant",
        "change-grant",
        "revoke-grant",
    ];

    public static TheoryData<int> BadPageSizes() => [0, -1, 101, 1000, int.MaxValue];

    public static TheoryData<string, string> MalformedBodies()
    {
        var data = new TheoryData<string, string>();
        foreach (var route in new[] { "create-grant", "change-grant", "update-secret", "reveal-secret", "file-secret" })
        {
            foreach (var body in new[] { string.Empty, "{", "null", "[]", "\"x\"", "123", "{\"recipientId\":\"x\"}", "{\"permission\":\"View\",}", "\u0000", "{\"field\":[1]}", "{\"folderId\":\"x\"}" })
            {
                data.Add(route, body);
            }
        }

        return data;
    }

    private WebApplicationFactory<Program> Limited(string setting, int perMinute) =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting($"VaultRateLimits:{setting}", perMinute.ToString(CultureInfo.InvariantCulture)));

    private static string RouteFor(string name, Guid owner, string item, string recipient) => name switch
    {
        "shared-secret" => Fill(ApiRoutes.SharedSecret, owner.ToString("D"), item, recipient),
        "shared-secret-reveal" => Fill(ApiRoutes.SharedSecretReveal, owner.ToString("D"), item, recipient),
        "shared-folder" => Fill(ApiRoutes.SharedFolder, owner.ToString("D"), item, recipient),
        "secret-grants" => Fill(ApiRoutes.SecretGrants, owner.ToString("D"), item, recipient),
        "secret-grant" => Fill(ApiRoutes.SecretGrant, owner.ToString("D"), item, recipient),
        "folder-grants" => Fill(ApiRoutes.FolderGrants, owner.ToString("D"), item, recipient),
        "folder-grant" => Fill(ApiRoutes.FolderGrant, owner.ToString("D"), item, recipient),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static string Fill(string template, string owner, string item, string recipient) => template
        .Replace("{ownerId:guid}", owner, StringComparison.Ordinal)
        .Replace("{secretId:guid}", item, StringComparison.Ordinal)
        .Replace("{folderId:guid}", item, StringComparison.Ordinal)
        .Replace("{recipientId:guid}", recipient, StringComparison.Ordinal);

    [Theory]
    [MemberData(nameof(RejectedSearchTerms))]
    public async Task SearchDirectory_ShortOrWildcardTerm_Returns400(string term)
    {
        await WarmUpAsync(Owner);
        Factory.GraphDirectory.Clear();
        ResetCounters();

        var response = (await SharingFor(Owner).SearchDirectoryAsync(term)).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.GraphDirectory.Calls);
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Method != "GET");
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchDirectory_MissingTerm_Returns400()
    {
        var response = await ClientFor(Owner).GetAsync(ApiRoutes.DirectoryUsers, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchDirectory_ODataSyntax_NeverReachesStorage()
    {
        await WarmUpAsync(Owner);
        Factory.GraphDirectory.Clear();
        ResetCounters();

        var response = (await SharingFor(Owner).SearchDirectoryAsync(ODataTerm)).Response;

        Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest, $"Unexpected {response.StatusCode}.");
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
        Assert.DoesNotContain(StorageRequests.Requests, request =>
            request.Path.Contains("PartitionKey ne", StringComparison.OrdinalIgnoreCase)
            || (request.Filter ?? string.Empty).Contains("PartitionKey ne", StringComparison.OrdinalIgnoreCase)
            || request.Path.Contains("%20or%20", StringComparison.OrdinalIgnoreCase));
        foreach (var graphRequest in Factory.GraphDirectory.Requests)
        {
            var search = System.Web.HttpUtility.ParseQueryString(new Uri("https://graph.test" + graphRequest).Query)["$search"] ?? string.Empty;
            Assert.StartsWith("\"displayName:", search, StringComparison.Ordinal);
            Assert.EndsWith("\"", search, StringComparison.Ordinal);
            Assert.Equal(4, search.Count(character => character == '"'));
        }
    }

    [Fact]
    public async Task SearchDirectory_OverRateLimit_Returns429()
    {
        await using var host = Limited("DirectorySearchesPerMinute", 3);
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        var api = new SharingTestApi(client);

        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.OK, (await api.SearchDirectoryAsync("sharpvault")).Response.StatusCode);
        }

        var limited = (await api.SearchDirectoryAsync("sharpvault")).Response;

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter);
        Assert.DoesNotContain(Owner.ObjectId.ToString("D"), await limited.Content.ReadAsStringAsync(Token), StringComparison.OrdinalIgnoreCase);
        using var other = CreateClientFor(host, TestTokens.Create(TestUsers.OtherUser));
        Assert.Equal(HttpStatusCode.OK, (await new SharingTestApi(other).SearchDirectoryAsync("sharpvault")).Response.StatusCode);
    }

    [Fact]
    public async Task CreateGrant_OverRateLimit_Returns429()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await using var host = Limited("ShareWritesPerMinute", 3);
        using var client = CreateClientFor(host, TestTokens.Create(Owner));
        var api = new SharingTestApi(client);
        var recipients = new[] { TestUsers.ShareViewer, TestUsers.ShareEditor, TestUsers.ShareManager, TestUsers.FolderViewer };

        for (var request = 0; request < 3; request++)
        {
            Assert.Equal(HttpStatusCode.Created, (await api.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(recipients[request], SharePermissionDto.View))).Response.StatusCode);
        }

        var limited = (await api.CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(recipients[3], SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.FolderViewer, secret.Id));
        Assert.Equal(3, (await SharingFor(Owner).ListSecretGrantsAsync(Owner.ObjectId, secret.Id)).Body!.Items.Count);
    }

    [Theory]
    [MemberData(nameof(NonGuidRoutes))]
    public async Task SharingRoutes_NonGuidIds_Return404(string route)
    {
        using var client = ClientFor(Owner);
        var good = Guid.NewGuid().ToString("D");
        foreach (var bad in new[] { "not-a-guid", "%27%20or%201%3D1", "..%2F..", "12345" })
        {
            var candidates = new[]
            {
                RouteFor(route, Guid.NewGuid(), bad, good),
                RouteFor(route, Guid.NewGuid(), good, bad),
            };
            foreach (var path in candidates.Where(candidate => candidate.Contains(bad, StringComparison.Ordinal)))
            {
                using var request = new HttpRequestMessage(route switch { "secret-grant" or "folder-grant" => HttpMethod.Delete, "shared-secret-reveal" => HttpMethod.Post, _ => HttpMethod.Get }, path);
                var response = await client.SendAsync(request, Token);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            }
        }

        foreach (var bad in new[] { "not-a-guid", "%27%20or%201%3D1" })
        {
            var ownerSegment = ApiRoutes.SharedSecret.Replace("{ownerId:guid}", bad, StringComparison.Ordinal).Replace("{secretId:guid}", good, StringComparison.Ordinal);
            var response = await client.GetAsync(ownerSegment, Token);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }

    [Theory]
    [MemberData(nameof(EmptyGuidRoutes))]
    public async Task GuidEmpty_Ids_Return400(string route)
    {
        var api = SharingFor(Owner);
        var empty = Guid.Empty;
        var live = Guid.NewGuid();

        var response = route switch
        {
            "get-secret" => (await api.GetSharedSecretAsync(empty, live)).Response,
            "update-secret" => (await api.UpdateSharedSecretAsync(live, empty, "\"W/abc\"", RenameBody("x"))).Response,
            "reveal-secret" => (await api.RevealSharedSecretAsync(empty, live, ViewValue)).Response,
            "get-folder" => (await api.GetSharedFolderAsync(live, empty)).Response,
            "file-secret" => await api.FileSharedSecretAsync(empty, live, new FileSharedItemRequestDto { FolderId = null }),
            "list-grants" => (await api.ListSecretGrantsAsync(empty, live)).Response,
            "create-grant" => (await api.CreateSecretGrantAsync(live, empty, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response,
            "change-grant" => (await api.ChangeSecretGrantAsync(live, live, empty, "\"W/abc\"", new ChangeShareGrantRequestDto { Permission = SharePermissionDto.Edit })).Response,
            "revoke-grant" => await api.RevokeSecretGrantAsync(live, live, empty),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GuidEmpty_RecipientInBody_Returns400()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, new CreateShareGrantRequestDto { RecipientId = Guid.Empty, Permission = SharePermissionDto.View })).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateGrant_At100_Returns400()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await SeedGrantsAsync(secret.Id, 100);
        var before = await SnapshotAsync();

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
        Assert.Equal(HttpStatusCode.NotFound, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
    }

    [Fact]
    public async Task CreateGrant_At99_Succeeds()
    {
        var secret = await NewSecretAsync(new PlaintextMarkers());
        await SeedGrantsAsync(secret.Id, 99);

        var response = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareViewer, SharePermissionDto.View))).Response;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await SharedGetStatusAsync(TestUsers.ShareViewer, secret.Id));
        var next = (await SharingFor(Owner).CreateSecretGrantAsync(Owner.ObjectId, secret.Id, GrantBody(TestUsers.ShareEditor, SharePermissionDto.View))).Response;
        Assert.Equal(HttpStatusCode.BadRequest, next.StatusCode);
    }

    [Theory]
    [MemberData(nameof(BadPageSizes))]
    public async Task ListSharedItems_PageSizeOver100OrZero_Returns400(int pageSize)
    {
        var folder = await NewFolderAsync("folder");
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var api = SharingFor(TestUsers.FolderViewer);

        Assert.Equal(HttpStatusCode.BadRequest, (await api.ListSharedItemsAsync(pageSize: pageSize)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.GetSharedFolderAsync(Owner.ObjectId, folder.Id, pageSize: pageSize)).Response.StatusCode);
    }

    [Fact]
    public async Task ListSharedItems_PageSizeOneAndOneHundred_AreAccepted()
    {
        var folder = await NewFolderAsync("folder");
        await ShareFolderAsync(folder.Id, TestUsers.FolderViewer, SharePermissionDto.View);
        var api = SharingFor(TestUsers.FolderViewer);

        Assert.Equal(HttpStatusCode.OK, (await api.ListSharedItemsAsync(pageSize: 1)).Response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.ListSharedItemsAsync(pageSize: 100)).Response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task Sharing_MalformedBody_Returns400(string route, string body)
    {
        var scenario = await SharingScenario.ArrangeAsync(ClientFor);
        var before = await SnapshotAsync();
        var vault = ApiFor(Owner);

        var response = route switch
        {
            "create-grant" => await vault.PostRawAsync(VaultTestApi.Bind(ApiRoutes.SecretGrants, ("ownerId", scenario.OwnerId), ("secretId", scenario.SecretS)), body),
            "change-grant" => await vault.PutRawAsync(VaultTestApi.Bind(ApiRoutes.SecretGrant, ("ownerId", scenario.OwnerId), ("secretId", scenario.SecretS), ("recipientId", TestUsers.ShareViewer.ObjectId)), body, scenario.ViewerGrantToken),
            "update-secret" => await ApiFor(TestUsers.ShareEditor).PutRawAsync(VaultTestApi.Bind(ApiRoutes.SharedSecret, ("ownerId", scenario.OwnerId), ("secretId", scenario.SecretS)), body, scenario.SecretToken),
            "reveal-secret" => await ApiFor(TestUsers.ShareViewer).PostRawAsync(VaultTestApi.Bind(ApiRoutes.SharedSecretReveal, ("ownerId", scenario.OwnerId), ("secretId", scenario.SecretS)), body),
            "file-secret" => await ApiFor(TestUsers.ShareViewer).PutRawAsync(VaultTestApi.Bind(ApiRoutes.SharedSecretFiling, ("ownerId", scenario.OwnerId), ("secretId", scenario.SecretS)), body),
            _ => throw new ArgumentOutOfRangeException(nameof(route), route, null),
        };

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before.Rows, (await SnapshotAsync()).Rows);
        var text = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("   at ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EntraMode_RegistersGraphDirectory()
    {
        _ = Factory.Server;

        var directory = Factory.Services.GetRequiredService<IUserDirectory>();

        Assert.IsType<SharpVault.Infrastructure.Directory.GraphUserDirectory>(directory);
    }

    private async Task SeedGrantsAsync(Guid secretId, int count)
    {
        _ = Factory.Server;
        var world = new PurgeGrantWorld(
            Factory.Services.GetRequiredService<IVaultStore>(),
            Factory.Services.GetRequiredService<IShareInboxStore>(),
            GetTableClient(SharpVault.Infrastructure.Storage.TableNames.Vaults),
            GetTableClient(SharpVault.Infrastructure.Storage.TableNames.ShareInbox),
            Owner.ObjectId);
        await world.SeedGrantsAsync(secretId, count, Token);
    }
}
