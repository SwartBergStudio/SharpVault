// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SharpVault.Dtos.Users;
using SharpVault.Infrastructure.Vaults;
using SharpVault.IntegrationTests.ApiTests.Sharing;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.IntegrationTests.Vault;

namespace SharpVault.IntegrationTests.ApiTests;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class UserCountsResponseTests(ApiTestFactory factory) : SharingFunctionalTestBase(factory)
{
    private const string MalformedUserId = "not-a-guid";
    private const string CountsPathPrefix = "/api/admin/users/";
    private const string CountsPathSuffix = "/counts";
    private const string ItemCountProperty = "itemCount";
    private const string ShareCountProperty = "shareCount";
    private const string CountsCappedProperty = "countsAreCapped";

    private static readonly string[] ExpectedFields = [ItemCountProperty, ShareCountProperty, CountsCappedProperty];

    [Fact]
    public async Task GetUserCounts_EmptyVault_ReturnsZeros()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.OtherUser);

        using var client = ClientFor(TestUsers.Admin);
        var counts = await GetCountsAsync(client, TestUsers.OtherUser.ObjectId);

        Assert.Equal(0, counts.ItemCount);
        Assert.Equal(0, counts.ShareCount);
        Assert.False(counts.CountsAreCapped);
    }

    [Fact]
    public async Task GetUserCounts_MalformedId_Returns404()
    {
        await WarmUpAsync(TestUsers.Admin);

        using var client = ClientFor(TestUsers.Admin);
        var response = await client.GetAsync(CountsPathPrefix + MalformedUserId + CountsPathSuffix, Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetUserCounts_ResponseBody_HasOnlyTheCountFieldsAndNoPlaintextMarker()
    {
        var markers = await SeedOwnerWithMarkersAsync();

        using var client = ClientFor(TestUsers.Admin);
        var response = await client.GetAsync(CountsPath(OwnerId), Token);
        var body = await response.Content.ReadAsStringAsync(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        markers.AssertAbsentFrom(body);
        using var document = JsonDocument.Parse(body);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedFields.Order(StringComparer.Ordinal), names);
    }

    [Fact]
    public async Task GetUserCounts_Requests_LogNoNamesOrCounts()
    {
        var markers = await SeedOwnerWithMarkersAsync();
        Logs.Clear();

        using var client = ClientFor(TestUsers.Admin);
        await client.GetAsync(CountsPath(OwnerId), Token);

        foreach (var marker in markers.All)
        {
            Assert.False(Logs.ContainsText(marker));
        }
    }

    [Fact]
    public async Task GetUserCounts_WritesNoAuditRow()
    {
        await SeedOwnerWithMarkersAsync();
        var before = await ListAuditRowsAsync();

        using var client = ClientFor(TestUsers.Admin);
        await client.GetAsync(CountsPath(OwnerId), Token);

        Assert.Equal(before.Count, (await ListAuditRowsAsync()).Count);
    }

    [Fact]
    public async Task GetUserCounts_ReadsNoKeyRingAndUnwrapsNothing()
    {
        await SeedOwnerWithMarkersAsync();
        var unwrapsBefore = Factory.KeyWraps.Unwraps;
        Factory.StorageRequests.Clear();

        using var client = ClientFor(TestUsers.Admin);
        var response = await client.GetAsync(CountsPath(OwnerId), Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(Factory.StorageRequests.Requests, request => request.Path.Contains($"RowKey='{VaultKeys.KeyRingRowKey}'", StringComparison.Ordinal));
        Assert.Equal(unwrapsBefore, Factory.KeyWraps.Unwraps);
    }

    private async Task<PlaintextMarkers> SeedOwnerWithMarkersAsync()
    {
        await WarmUpAsync(TestUsers.Admin);
        await WarmUpAsync(TestUsers.User);
        var markers = new PlaintextMarkers();
        await NewSecretAsync(markers);
        return markers;
    }

    private static async Task<UserCountsDto> GetCountsAsync(HttpClient client, Guid userId)
    {
        var response = await client.GetAsync(CountsPath(userId), Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var counts = await response.Content.ReadFromJsonAsync<UserCountsDto>(Token);
        return Assert.IsType<UserCountsDto>(counts);
    }

    private static string CountsPath(Guid userId) => CountsPathPrefix + userId.ToString("D") + CountsPathSuffix;
}
