// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.Dtos;
using SharpVault.UI.Shared.Services;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class AdminUsersApiClientTests : IDisposable
{
    private const string PageJson = """{"users":[{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice","status":"Active"}],"continuationToken":"next"}""";

    private readonly StubHttpHandler _handler = new();
    private readonly AdminUsersApiClient _client;

    public AdminUsersApiClientTests()
    {
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer("t"));
        var authentication = Substitute.For<AuthenticationStateProvider>();
        authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.Admin"))));
        var session = Substitute.For<ISessionValidator>();
        session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        _client = new AdminUsersApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.example.test/") }, tokens, authentication, session, NullLogger<AdminUsersApiClient>.Instance);
        _handler.Respond(HttpStatusCode.OK, PageJson);
    }

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task List_NoArguments_CallsRouteWithoutQuery()
    {
        var response = await _client.ListUsersAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(ApiRoutes.AdminUsers, request.Uri.AbsolutePath);
        Assert.Equal(string.Empty, request.Uri.Query);
    }

    [Fact]
    public async Task List_PageSizeAndToken_AreEscapedQueryValues()
    {
        await _client.ListUsersAsync(25, "abc", TestContext.Current.CancellationToken);

        Assert.Equal($"?{ApiQuery.PageSize}=25&{ApiQuery.ContinuationToken}=abc", Assert.Single(_handler.Requests).Uri.Query);
    }

    [Fact]
    public async Task List_TokenWithSpecialCharacters_IsEscaped()
    {
        const string token = "a b&c=d/e?f#g%h+i";

        await _client.ListUsersAsync(null, token, TestContext.Current.CancellationToken);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal($"?{ApiQuery.ContinuationToken}={Uri.EscapeDataString(token)}", request.Uri.Query);
        Assert.DoesNotContain('&', request.Uri.Query);
    }

    [Fact]
    public async Task List_UnknownStatusText_ReturnsFailed()
    {
        _handler.Respond(HttpStatusCode.OK, """{"users":[{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice","status":"Banished"}],"continuationToken":null}""");

        var response = await _client.ListUsersAsync(null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task List_EmptyToken_IsOmitted()
    {
        await _client.ListUsersAsync(10, string.Empty, TestContext.Current.CancellationToken);

        Assert.Equal($"?{ApiQuery.PageSize}=10", Assert.Single(_handler.Requests).Uri.Query);
    }
}
