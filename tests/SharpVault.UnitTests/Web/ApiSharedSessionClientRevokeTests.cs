// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class ApiSharedSessionClientRevokeTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private static readonly ClaimsPrincipal Principal = new(new ClaimsIdentity(
        [
            new Claim(SessionClaims.SessionId, Guid.NewGuid().ToString("N")),
            new Claim(SessionClaims.ExpiresAt, DateTimeOffset.UtcNow.AddHours(8).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)),
        ],
        "Test"));

    [Fact]
    public async Task RevokeAsync_TokenProviderHangs_ReturnsWithinTheLimitAndDoesNotThrow()
    {
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(_ => new TaskCompletionSource<ApiAccessToken>().Task);
        var client = new ApiSharedSessionClient(new HttpClient(new HangingHandler()) { BaseAddress = new Uri("https://localhost/") }, tokens, NullLogger<ApiSharedSessionClient>.Instance);
        using var limit = new CancellationTokenSource(Limit);

        var revoke = client.RevokeAsync(Principal, limit.Token);

        Assert.Same(revoke, await Task.WhenAny(revoke, Task.Delay(Patience, TestContext.Current.CancellationToken)));
        await revoke;
    }

    [Fact]
    public async Task RevokeAsync_HttpHangs_ReturnsWithinTheLimitAndDoesNotThrow()
    {
        var tokens = Substitute.For<IApiAccessTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ApiAccessToken.Bearer("token")));
        var client = new ApiSharedSessionClient(new HttpClient(new HangingHandler()) { BaseAddress = new Uri("https://localhost/") }, tokens, NullLogger<ApiSharedSessionClient>.Instance);
        using var limit = new CancellationTokenSource(Limit);

        var revoke = client.RevokeAsync(Principal, limit.Token);

        Assert.Same(revoke, await Task.WhenAny(revoke, Task.Delay(Patience, TestContext.Current.CancellationToken)));
        await revoke;
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
