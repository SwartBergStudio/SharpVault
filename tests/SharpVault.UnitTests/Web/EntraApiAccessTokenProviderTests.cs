// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharpVault.UnitTests.Support;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Api;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class EntraApiAccessTokenProviderTests
{
    private const string Token = "eyJ.entra-access-token.signature";

    private static readonly string[] Scopes = ["api://a1b2c3d4/access_as_user"];

    private readonly ITokenAcquisition _tokens = Substitute.For<ITokenAcquisition>();
    private readonly ClaimsPrincipal _principal = TestIdentities.Authenticated(TestIdentities.MemberClaims(Guid.NewGuid(), "SharpVault.User"));

    private EntraApiAccessTokenProvider CreateProvider() =>
        new(_tokens, Options.Create(new SharpVaultApiOptions { BaseAddress = new Uri("https://api.example.test/"), Scopes = Scopes }));

    private void AcquisitionReturns(string token) =>
        _tokens.GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .Returns(token);

    private void AcquisitionThrows(Exception exception) =>
        _tokens.GetAccessTokenForUserAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<ClaimsPrincipal?>(),
                Arg.Any<TokenAcquisitionOptions?>())
            .ThrowsAsync(exception);

    [Fact]
    public async Task GetAccessToken_AcquisitionSucceeds_ReturnsBearer()
    {
        AcquisitionReturns(Token);

        var result = await CreateProvider().GetAccessTokenAsync(_principal, TestContext.Current.CancellationToken);

        Assert.Equal(ApiAccessTokenKind.Bearer, result.Kind);
        Assert.Equal(Token, result.Value);
    }

    [Fact]
    public async Task GetAccessToken_ChallengeException_ReturnsSignInRequired()
    {
        AcquisitionThrows(new MicrosoftIdentityWebChallengeUserException(new MsalUiRequiredException("code", "message with secret"), ["scope"]));

        var result = await CreateProvider().GetAccessTokenAsync(_principal, TestContext.Current.CancellationToken);

        Assert.Equal(ApiAccessTokenKind.SignInRequired, result.Kind);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetAccessToken_MsalUiRequired_ReturnsSignInRequired()
    {
        AcquisitionThrows(new MsalUiRequiredException("code", "message with secret"));

        var result = await CreateProvider().GetAccessTokenAsync(_principal, TestContext.Current.CancellationToken);

        Assert.Equal(ApiAccessTokenKind.SignInRequired, result.Kind);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task GetAccessToken_OtherException_IsNotSwallowed()
    {
        AcquisitionThrows(new InvalidOperationException("token cache unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateProvider().GetAccessTokenAsync(_principal, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAccessToken_Acquisition_RequestsConfiguredScopesForThatPrincipal()
    {
        AcquisitionReturns(Token);

        var cancellation = TestContext.Current.CancellationToken;
        await CreateProvider().GetAccessTokenAsync(_principal, cancellation);

        await _tokens.Received(1).GetAccessTokenForUserAsync(
            Arg.Is<IEnumerable<string>>(scopes => scopes.SequenceEqual(Scopes)),
            null,
            null,
            null,
            _principal,
            Arg.Is<TokenAcquisitionOptions>(options => options.CancellationToken == cancellation));
    }

    [Fact]
    public void InteractiveSignInAvailable_IsTrue()
    {
        Assert.True(CreateProvider().InteractiveSignInAvailable);
    }
}
