// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.Dtos;
using SharpVault.Dtos.Transfers;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;

namespace SharpVault.IntegrationTests.ApiTests.Transfers;

[Collection(nameof(StorageTestGroup))]
[Trait("Category", "Security")]
public sealed class OwnedSharesPagingTests(ApiTestFactory factory) : TransferTestBase(factory)
{
    private const string ForeignStorageToken = "tZm9vYmFy";

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("t")]
    [InlineData("t!!!")]
    [InlineData("0123456789abcdef0123456789abcdef")]
    [InlineData("f_0123456789abcdef0123456789abcdef")]
    [InlineData("")]
    public async Task OwnedShares_MalformedContinuationToken_Returns400WithoutInternals(string token)
    {
        await WarmUpUserAsync(TestUsers.User);

        var response = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, OwnedSharesPath(token), null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync(Token);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OwnedShares_WellFormedForeignToken_IsNotAServerError()
    {
        await WarmUpUserAsync(TestUsers.User);

        var response = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, OwnedSharesPath(ForeignStorageToken), null, null);

        Assert.True((int)response.StatusCode < 500, $"Foreign token returned {response.StatusCode}.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task OwnedShares_InvalidPageSize_Returns400(string pageSize)
    {
        await WarmUpUserAsync(TestUsers.User);

        var response = await ApiAs(TestUsers.User).SendRawAsync(HttpMethod.Get, $"{ApiRoutes.TransferOwnedShares}?pageSize={pageSize}", null, null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OwnedShares_CallerWithNoGrants_ReturnsAnEmptyPage()
    {
        await WarmUpUserAsync(TestUsers.User);

        var (response, page) = await ApiAs(TestUsers.User).SendAsync<OwnedSharePageDto>(HttpMethod.Get, ApiRoutes.TransferOwnedShares);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(page?.Items ?? []);
        Assert.Null(page?.ContinuationToken);
    }

    private static string OwnedSharesPath(string token) =>
        $"{ApiRoutes.TransferOwnedShares}?continuationToken={Uri.EscapeDataString(token)}";
}
