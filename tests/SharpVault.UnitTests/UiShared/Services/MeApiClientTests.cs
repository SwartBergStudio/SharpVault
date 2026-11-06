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

public sealed class MeApiClientTests : IDisposable
{
    private const string UserId = "3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b";

    private readonly StubHttpHandler _handler = new();
    private readonly MeApiClient _client;

    public MeApiClientTests()
    {
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer("t"));
        var authentication = Substitute.For<AuthenticationStateProvider>();
        authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"))));
        var session = Substitute.For<ISessionValidator>();
        session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        _client = new MeApiClient(new HttpClient(_handler) { BaseAddress = new Uri("https://api.example.test/") }, tokens, authentication, session, NullLogger<MeApiClient>.Instance);
    }

    public void Dispose() => _handler.Dispose();

    [Fact]
    public async Task Get_EmptyUserId_ReturnsFailed()
    {
        _handler.Respond(HttpStatusCode.OK, """{"userId":"00000000-0000-0000-0000-000000000000","displayName":"Alice","isAdmin":false}""");

        var response = await _client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("{\"userId\":\"not-a-guid\",\"displayName\":\"x\",\"isAdmin\":false}")]
    [InlineData("{\"userId\":\"00000000-0000-0000-0000-000000000000\",\"displayName\":\"x\",\"isAdmin\":false}")]
    [InlineData("{\"userId\":12,\"displayName\":{},\"isAdmin\":\"maybe\"}")]
    [InlineData("{}")]
    public async Task Get_MalformedBody_ReturnsFailedWithNoProfile(string body)
    {
        _handler.Respond(HttpStatusCode.OK, body);

        var response = await _client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
        Assert.Null(response.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Get_BlankName_ReturnsFailed(string name)
    {
        _handler.Respond(HttpStatusCode.OK, $$"""{"userId":"{{UserId}}","displayName":"{{name}}","isAdmin":false}""");

        var response = await _client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Failed, response.Status);
    }

    [Fact]
    public async Task Get_Valid_ReturnsDto()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""{"userId":"{{UserId}}","displayName":"Alice","isAdmin":true}""");

        var response = await _client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiCallStatus.Success, response.Status);
        Assert.Equal(Guid.Parse(UserId), response.Value!.UserId);
        Assert.Equal("Alice", response.Value.DisplayName);
        Assert.True(response.Value.IsAdmin);
    }

    [Fact]
    public async Task Get_UsesApiRoutesMe()
    {
        _handler.Respond(HttpStatusCode.OK, $$"""{"userId":"{{UserId}}","displayName":"Alice","isAdmin":true}""");

        await _client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ApiRoutes.Me, Assert.Single(_handler.Requests).Uri.AbsolutePath);
    }
}
