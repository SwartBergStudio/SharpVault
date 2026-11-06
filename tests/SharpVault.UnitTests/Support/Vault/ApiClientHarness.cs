// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpVault.UI.Shared.Services.Auth;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class ApiClientHarness : IDisposable
{
    public const string Token = "eyJ.vault-client-token.signature";

    public ApiClientHarness()
    {
        Handler = new StubHttpHandler();
        Tokens = Substitute.For<IApiAccessTokenProvider>();
        Tokens.InteractiveSignInAvailable.Returns(true);
        Tokens.GetAccessTokenAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(ApiAccessToken.Bearer(Token));
        Authentication = Substitute.For<AuthenticationStateProvider>();
        Authentication.GetAuthenticationStateAsync().Returns(new AuthenticationState(TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"))));
        Session = Substitute.For<ISessionValidator>();
        Session.IsActiveAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<CancellationToken>()).Returns(true);
        Http = new HttpClient(Handler) { BaseAddress = new Uri("https://api.example.test/") };
    }

    public StubHttpHandler Handler { get; }

    public IApiAccessTokenProvider Tokens { get; }

    public AuthenticationStateProvider Authentication { get; }

    public ISessionValidator Session { get; }

    public HttpClient Http { get; }

    public TClient Create<TClient>(Func<HttpClient, IApiAccessTokenProvider, AuthenticationStateProvider, ISessionValidator, ILogger<TClient>, TClient> factory) =>
        factory(Http, Tokens, Authentication, Session, NullLogger<TClient>.Instance);

    public void Dispose()
    {
        Http.Dispose();
        Handler.Dispose();
    }
}
