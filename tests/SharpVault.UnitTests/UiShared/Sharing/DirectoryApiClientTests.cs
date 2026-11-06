// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Sharing;

public sealed class DirectoryApiClientTests : IDisposable
{
    private readonly ApiClientHarness _harness = new();
    private readonly DirectoryApiClient _client;

    public DirectoryApiClientTests()
    {
        _client = _harness.Create<DirectoryApiClient>((http, tokens, auth, session, logger) => new DirectoryApiClient(http, tokens, auth, session, logger));
    }

    public void Dispose() => _harness.Dispose();

    private StubHttpHandler.RecordedRequest Only => Assert.Single(_harness.Handler.Requests);

    [Fact]
    public async Task Search_EscapesTheTermInTheQuery()
    {
        _harness.Handler.Respond(HttpStatusCode.OK, """{"items":[]}""");

        var response = await _client.SearchUsersAsync("a b&search=x#é/ '", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(HttpMethod.Get, Only.Method);
        Assert.Equal("/api/directory/users", Only.Uri.AbsolutePath);
        Assert.Equal($"?search={Uri.EscapeDataString("a b&search=x#é/ '")}", Only.Uri.Query);
    }

    [Fact]
    public async Task Search_Users_AreDeserialized()
    {
        var id = Guid.NewGuid();
        _harness.Handler.Respond(HttpStatusCode.OK, $$"""{"items":[{"id":"{{id:D}}","displayName":"Ada","email":null}]}""");

        var response = await _client.SearchUsersAsync("ad", TestContext.Current.CancellationToken);

        var user = Assert.Single(response.Value!.Items);
        Assert.Equal(id, user.Id);
        Assert.Equal("Ada", user.DisplayName);
        Assert.Null(user.Email);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Search_BlankTerm_ThrowsAndSendsNothing(string? term)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => _client.SearchUsersAsync(term!, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ApiCallStatus.SignInRequired)]
    [InlineData(HttpStatusCode.Forbidden, ApiCallStatus.AccessDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, ApiCallStatus.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, ApiCallStatus.Invalid)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ApiCallStatus.Failed)]
    public async Task Search_ErrorStatuses_MapToTheCallStatus(HttpStatusCode status, ApiCallStatus expected)
    {
        _harness.Handler.Respond(status, string.Empty);

        var response = await _client.SearchUsersAsync("ad", TestContext.Current.CancellationToken);

        Assert.Equal(expected, response.Status);
        Assert.Null(response.Value);
    }

    [Fact]
    public async Task Search_NetworkFailure_ReturnsFailedWithoutThrowing()
    {
        _harness.Handler.Throw(new HttpRequestException("boom"));

        var response = await _client.SearchUsersAsync("ad", TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }
}
