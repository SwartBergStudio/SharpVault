// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Text;
using Azure.Data.Tables;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Dtos;
using SharpVault.Dtos.Vault;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests.Vault;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class VaultInputTests(ApiTestFactory factory) : VaultSecurityTestBase(factory)
{
    private const string UrlPrefix = "https://x.test/";
    private const int TransactionSize = 100;

    private static readonly Guid Owner = TestUsers.User.ObjectId;

    public static TheoryData<string> OverLimitFields() =>
        ["name-201", "username-257", "url-2049", "value-8193", "notes-8193", "tag-51", "tags-21", "folder-name-101"];

    public static TheoryData<string> BadPageSizes() => ["0", "201", "-1"];

    public static TheoryData<string> ODataTokens() =>
    [
        "' or PartitionKey ne '",
        "x' or 1 eq 1 or 'x",
    ];

    private static readonly string WrappedODataToken =
        "t" + Convert.ToBase64String(Encoding.UTF8.GetBytes("' or PartitionKey ne '")).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Url(int length) => UrlPrefix + new string('a', length - UrlPrefix.Length);

    private static async Task<HttpResponseMessage> SendOverLimitAsync(VaultTestApi api, string field) => field switch
    {
        "name-201" => (await api.CreateSecretAsync(Create(name: new string('n', 201)))).Response,
        "username-257" => (await api.CreateSecretAsync(Create(username: new string('u', 257)))).Response,
        "url-2049" => (await api.CreateSecretAsync(Create(url: Url(2049)))).Response,
        "value-8193" => (await api.CreateSecretAsync(Create(value: new string('v', 8193)))).Response,
        "notes-8193" => (await api.CreateSecretAsync(Create(notes: new string('n', 8193)))).Response,
        "tag-51" => (await api.CreateSecretAsync(Create(tags: [new string('t', 51)]))).Response,
        "tags-21" => (await api.CreateSecretAsync(Create(tags: [.. Enumerable.Range(0, 21).Select(index => $"tag-{index}")]))).Response,
        "folder-name-101" => (await api.CreateFolderAsync(new CreateFolderRequestDto { Name = new string('f', 101) })).Response,
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
    };

    private static CreateSecretRequestDto Create(
        string name = "name",
        string? username = null,
        string? url = null,
        string? value = "value",
        string? notes = null,
        List<string>? tags = null) => new()
    {
        Type = SecretTypeDto.Login,
        Name = name,
        Username = username,
        Url = url,
        Value = value,
        Notes = notes,
        Tags = tags,
    };

    private static string RawBody(string nameJson, string valueJson = "\"v\"") =>
        $"{{\"type\":\"Login\",\"name\":{nameJson},\"value\":{valueJson}}}";

    private async Task RecycleFolderBatchAsync(Guid folderId, IEnumerable<Guid> secretIds)
    {
        var batch = VaultIds.DeletionBatchIdFor(folderId).ToString("D");
        var deletedAt = DateTimeOffset.UtcNow;
        var actions = new List<TableTransactionAction>();
        var partition = VaultKeys.PartitionKey(Owner);

        foreach (var secretId in secretIds)
        {
            var row = await VaultRows.GetAsync(VaultsTable, Owner, VaultKeys.SecretRowKey(secretId, recycled: false), Token);
            actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, row!));
            actions.Add(new TableTransactionAction(TableTransactionActionType.Add, Recycled(row!, VaultKeys.SecretRowKey(secretId, recycled: true), partition, batch, deletedAt)));
        }

        var folderRow = await VaultRows.GetAsync(VaultsTable, Owner, VaultKeys.FolderRowKey(folderId, recycled: false), Token);
        actions.Add(new TableTransactionAction(TableTransactionActionType.Delete, folderRow!));
        actions.Add(new TableTransactionAction(TableTransactionActionType.Add, Recycled(folderRow!, VaultKeys.FolderRowKey(folderId, recycled: true), partition, batch, deletedAt)));

        foreach (var chunk in actions.Chunk(TransactionSize))
        {
            await VaultsTable.SubmitTransactionAsync(chunk, Token);
        }
    }

    private static TableEntity Recycled(TableEntity source, string rowKey, string partition, string batch, DateTimeOffset deletedAt)
    {
        var copy = new TableEntity(partition, rowKey);
        foreach (var property in source)
        {
            if (property.Key is not ("PartitionKey" or "RowKey" or "Timestamp" or "odata.etag"))
            {
                copy[property.Key] = property.Value;
            }
        }

        copy["DeletedAt"] = deletedAt;
        copy["DeletionBatchId"] = batch;
        return copy;
    }

    [Theory]
    [MemberData(nameof(OverLimitFields))]
    public async Task CreateSecret_FieldOverLimit_Returns400(string field)
    {
        var api = ApiFor(TestUsers.User);

        var response = await SendOverLimitAsync(api, field);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_FieldsAtLimit_Succeed()
    {
        var api = ApiFor(TestUsers.User);
        var tags = Enumerable.Range(0, 20).Select(index => $"{index:D2}" + new string('t', 48)).ToList();
        var request = Create(
            name: new string('n', 200),
            username: new string('u', 256),
            url: Url(2048),
            value: new string('v', 8192),
            notes: new string('o', 8192),
            tags: tags);

        var created = await api.CreateSecretAsync(request);
        var folder = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = new string('f', 100) });

        Assert.Equal(HttpStatusCode.Created, created.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, folder.Response.StatusCode);
        var read = (await api.GetSecretAsync(created.Body!.Id)).Body!;
        Assert.Equal(request.Name, read.Name);
        Assert.Equal(request.Username, read.Username);
        Assert.Equal(request.Url, read.Url);
        Assert.Equal(tags, read.Tags);
        Assert.Equal(request.Value, await RevealValueAsync(api, created.Body.Id));
        Assert.Equal(request.Notes, (await api.RevealSecretAsync(created.Body.Id, ViewNotes)).Body!.Value);
        Assert.Equal(new string('f', 100), folder.Body!.Name);
    }

    [Fact]
    public async Task CreateSecret_BodyOver1MiB_Returns413()
    {
        using var kestrel = Factory.WithWebHostBuilder(_ => { });
        kestrel.UseKestrel(0);
        kestrel.StartServer();
        using var client = kestrel.CreateClient();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", TestTokens.Create(TestUsers.User));
        var api = new VaultTestApi(client);
        var json = RawBody("\"n\"", "\"" + new string('v', 1_100_000) + "\"");

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, json);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Theory]
    [MemberData(nameof(BadPageSizes))]
    public async Task ListSecrets_PageSizeZeroOrOverMax_Returns400(string pageSize)
    {
        var api = ApiFor(TestUsers.User);
        await CreateSecretAsync(api, new PlaintextMarkers());

        var response = await api.SendRawAsync(HttpMethod.Get, $"{ApiRoutes.VaultSecrets}?{ApiQuery.PageSize}={pageSize}", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_VaultOf6000_ScansAtMost5000RowsPerRequestAndReturnsToken()
    {
        var api = ApiFor(TestUsers.User);
        await WarmUpAsync(TestUsers.User);
        await VaultSeeder.SeedAsync(Factory, Owner, 6000, 0, Token);
        ResetCounters();

        var result = await api.ListSecretsAsync(search: "no-secret-has-this-term-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Empty(result.Body!.Items);
        Assert.NotNull(result.Body.ContinuationToken);
        var queryPages = StorageRequests.Requests.Count(request => request.IsEntityQuery && request.Path.Contains("/Vaults", StringComparison.Ordinal));
        Assert.InRange(queryPages, 1, 5);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task UpdateValue_TwentyFiveTimes_KeepsTwentyNewestVersions()
    {
        var api = ApiFor(TestUsers.User);
        var markers = new PlaintextMarkers();
        var secret = await CreateSecretAsync(api, markers);
        var token = secret.ConcurrencyToken;
        for (var index = 1; index <= 25; index++)
        {
            token = Require(await api.UpdateSecretAsync(secret.Id, token, UpdateRequest(markers, $"value-{index}"))).ConcurrencyToken;
        }

        var history = Require(await api.ListHistoryAsync(secret.Id));

        Assert.Equal(20, history.Items.Count);
        var versionRows = (await VaultRows.ListPartitionAsync(VaultsTable, Owner, Token))
            .Count(row => row.RowKey.StartsWith(VaultKeys.VersionRangeStart(secret.Id), StringComparison.Ordinal));
        Assert.Equal(20, versionRows);
        var newest = await api.RevealVersionAsync(secret.Id, history.Items[0].VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View });
        var oldest = await api.RevealVersionAsync(secret.Id, history.Items[^1].VersionId, new RevealVersionRequestDto { Intent = RevealIntentDto.View });
        Assert.Equal("value-24", newest.Body!.Value);
        Assert.Equal("value-5", oldest.Body!.Value);
        Assert.Equal("value-25", await RevealValueAsync(api, secret.Id));
    }

    [Fact]
    public async Task ListSecrets_SearchWithODataSyntax_IsNeverSentToStorage()
    {
        var api = ApiFor(TestUsers.User);
        await CreateSecretAsync(api, new PlaintextMarkers());
        ResetCounters();

        var result = await api.ListSecretsAsync(search: "' or PartitionKey ne '");

        Assert.Equal(HttpStatusCode.OK, result.Response.StatusCode);
        Assert.Empty(result.Body!.Items);
        Assert.NotEmpty(StorageRequests.VaultReads());
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Filter?.Contains(" or PartitionKey ne", StringComparison.OrdinalIgnoreCase) == true);
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Path.Contains("PartitionKey%20ne", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Theory]
    [MemberData(nameof(ODataTokens))]
    public async Task ListSecrets_ContinuationTokenWithODataSyntax_Returns400(string token)
    {
        var api = ApiFor(TestUsers.User);
        await CreateSecretAsync(api, new PlaintextMarkers());

        var result = await api.ListSecretsAsync(continuationToken: token);

        Assert.Equal(HttpStatusCode.BadRequest, result.Response.StatusCode);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task ListSecrets_WrappedODataStorageToken_NeverReachesTheFilter()
    {
        var api = ApiFor(TestUsers.User);
        await CreateSecretAsync(api, new PlaintextMarkers());
        ResetCounters();

        var result = await api.ListSecretsAsync(continuationToken: WrappedODataToken);

        Assert.True(result.Response.StatusCode is HttpStatusCode.OK or HttpStatusCode.BadRequest);
        Assert.NotEmpty(StorageRequests.VaultReads());
        Assert.DoesNotContain(StorageRequests.Requests, request => request.Filter?.Contains("ne '", StringComparison.Ordinal) == true);
        Assert.Empty(StorageRequests.QueriesWithoutPartitionKey());
    }

    [Fact]
    public async Task CreateSecret_LoneSurrogateEscape_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var first = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"\\ud800\""));
        var second = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"ok\"", "\"\\udc00\""));

        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_ControlCharacterInName_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"bad\\u0001name\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_BidiOverrideInName_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"safe\\u202Etxt.exe\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_DecomposedUnicodeValue_RoundTripsByteExact()
    {
        var api = ApiFor(TestUsers.User);
        var value = "cafe\u0301 A\u030A \u0041\u0308 \u1100\u1161\u11A8";
        var secret = Require(await api.CreateSecretAsync(Create(value: value)));

        var revealed = await RevealValueAsync(api, secret.Id);

        Assert.Equal(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(revealed));
        Assert.NotEqual(value.Normalize(NormalizationForm.FormC), revealed);
    }

    [Fact]
    public async Task CreateSecret_EmojiAndNewlinesInValue_RoundTrip()
    {
        var api = ApiFor(TestUsers.User);
        var value = "line1\r\nline2\nline3\t\U0001F600\U0001F468\u200D\U0001F469\u200D\U0001F467 end";
        var secret = Require(await api.CreateSecretAsync(Create(value: value, notes: "n1\nn2\r\n\U0001F512")));

        Assert.Equal(value, await RevealValueAsync(api, secret.Id));
        Assert.Equal("n1\nn2\r\n\U0001F512", (await api.RevealSecretAsync(secret.Id, ViewNotes)).Body!.Value);
    }

    [Fact]
    public async Task CreateSecret_WhitespaceOnlyName_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"   \""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_EmptyValueString_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = await api.PostRawAsync(ApiRoutes.VaultSecrets, RawBody("\"n\"", "\"\""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateFolder_NameWithSlashHashQuestionBackslash_Succeeds()
    {
        var api = ApiFor(TestUsers.User);
        const string name = "a/b\\c#d?e";

        var folder = await api.CreateFolderAsync(new CreateFolderRequestDto { Name = name });
        var secret = await api.CreateSecretAsync(Create(name: name, tags: ["t/#?\\"]));

        Assert.Equal(HttpStatusCode.Created, folder.Response.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secret.Response.StatusCode);
        Assert.Equal(name, Assert.Single((await api.ListFoldersAsync()).Body!.Items).Name);
        var read = (await api.GetSecretAsync(secret.Body!.Id)).Body!;
        Assert.Equal(name, read.Name);
        Assert.Equal(["t/#?\\"], read.Tags);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("//evil.test/")]
    public async Task CreateSecret_NonHttpUrl_Returns400(string url)
    {
        var api = ApiFor(TestUsers.User);

        var response = (await api.CreateSecretAsync(Create(url: url))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task CreateSecret_JavascriptUrl_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = (await api.CreateSecretAsync(Create(url: "javascript:alert(document.cookie)"))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("javascript", await BodyOfAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CreateSecret_DataUrl_Returns400()
    {
        var api = ApiFor(TestUsers.User);

        var response = (await api.CreateSecretAsync(Create(url: "data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg=="))).Response;

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("data:", await BodyOfAsync(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DeleteFolder_MoreThan1000Items_Returns400AndWritesNothing()
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "big" }));
        await VaultSeeder.SeedAsync(Factory, Owner, 1001, 0, new SeedPlacement(null, folder.Id), Token);
        var before = await RowsOfAsync(Owner);
        ResetCounters();

        var moveToParent = await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.MoveToParent);
        var deleteContents = await api.DeleteFolderAsync(folder.Id, FolderDeleteModeDto.DeleteContents);

        Assert.Equal(HttpStatusCode.BadRequest, moveToParent.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, deleteContents.StatusCode);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, (await ProblemOfAsync(moveToParent)).GetProperty("detail").GetString());
        Assert.Empty(StorageRequests.VaultWrites());
        Assert.Equal(before, await RowsOfAsync(Owner));
    }

    [Fact]
    public async Task PurgeFolder_MoreThan1000Items_Returns400()
    {
        var api = ApiFor(TestUsers.User);
        var folder = Require(await api.CreateFolderAsync(new CreateFolderRequestDto { Name = "big" }));
        var seeded = await VaultSeeder.SeedAsync(Factory, Owner, 1001, 0, new SeedPlacement(null, folder.Id), Token);
        await RecycleFolderBatchAsync(folder.Id, seeded.Secrets.Select(secret => secret.Id));
        var before = await RowsOfAsync(Owner);

        var response = await api.PurgeRecycledFolderAsync(folder.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(VaultMessages.FolderOperationTooLarge, (await ProblemOfAsync(response)).GetProperty("detail").GetString());
        Assert.Equal(before, await RowsOfAsync(Owner));
    }
}
