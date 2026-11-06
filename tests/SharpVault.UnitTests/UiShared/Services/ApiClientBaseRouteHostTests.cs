// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Net;
using NSubstitute;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.UiShared.Services;

[Trait("Category", "Security")]
public sealed class ApiClientBaseRouteHostTests : IDisposable
{
    private const string ProfileJson = """{"userId":"3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b","displayName":"Alice Example","isAdmin":true}""";

    private readonly ApiClientHarness _harness = new();
    private readonly ApiClientBaseTests.ProbeClient _client;

    public ApiClientBaseRouteHostTests()
    {
        _client = _harness.Create<ApiClientBaseTests.ProbeClient>((http, tokens, auth, session, logger) => new ApiClientBaseTests.ProbeClient(http, tokens, auth, session, logger));
        _harness.Handler.Respond(HttpStatusCode.OK, ProfileJson);
    }

    public void Dispose() => _harness.Dispose();

    [Theory]
    [InlineData("https://evil.example.test/steal")]
    [InlineData("http://evil.example.test/steal")]
    [InlineData("HTTPS://EVIL.EXAMPLE.TEST/steal")]
    [InlineData("//evil.example.test/steal")]
    [InlineData("///evil.example.test/steal")]
    [InlineData("/\\evil.example.test/steal")]
    [InlineData("\\\\evil.example.test/steal")]
    [InlineData("\\/evil.example.test/steal")]
    [InlineData("/\t/evil.example.test/steal")]
    [InlineData("evil.example.test/steal")]
    public async Task Send_AbsoluteOrSchemeRelativeRoute_NeverSendsTheTokenToAnotherHost(string route)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _client.ReadAsync(route, TestContext.Current.CancellationToken));

        Assert.Empty(_harness.Handler.Requests);
        await _harness.Tokens.DidNotReceiveWithAnyArgs().GetAccessTokenAsync(default!, TestContext.Current.CancellationToken);
        Assert.All(_harness.Handler.Requests, request =>
        {
            Assert.Equal("api.example.test", request.Uri.Host);
            Assert.Equal(Uri.UriSchemeHttps, request.Uri.Scheme);
        });
    }
}
