// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

/// <summary>
/// W8 and W9: an ID token from another tenant is rejected and identity provider errors are never shown.
/// </summary>
[Trait("Category", "Security")]
public sealed class WebTokenValidationTests : IDisposable
{
    private readonly CapturingLoggerProvider _logs = new();

    public void Dispose() => _logs.Dispose();

    private DefaultHttpContext HttpContextWithLogging()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(new LoggerFactory([_logs]));
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private TokenValidatedContext TokenContext(System.Security.Claims.ClaimsPrincipal principal) =>
        new(
            HttpContextWithLogging(),
            new AuthenticationScheme("OpenIdConnect", null, typeof(OpenIdConnectHandler)),
            new OpenIdConnectOptions(),
            principal,
            new AuthenticationProperties());

    [Fact]
    public async Task ValidateIdTokenAsync_OwnTenant_DoesNotFail()
    {
        var context = TokenContext(TestIdentities.Authenticated(TestIdentities.AccessTokenClaims()));

        await WebTokenValidation.ValidateIdTokenAsync(context, TestIdentities.Settings);

        Assert.Null(context.Result);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_OtherTenant_FailsAndLogsReasonOnly()
    {
        var foreign = TestIdentities.OtherTenantId.ToString("D");
        var claims = TestIdentities.AccessTokenClaims().Replacing("tid", foreign).With("name", "Alice Example");
        var context = TokenContext(TestIdentities.Authenticated(claims));

        await WebTokenValidation.ValidateIdTokenAsync(context, TestIdentities.Settings);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        var log = Assert.Single(_logs.Messages);
        Assert.Contains("WrongTenant", log, StringComparison.Ordinal);
        Assert.DoesNotContain(foreign, log, StringComparison.Ordinal);
        Assert.DoesNotContain("Alice", log, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("tid")]
    [InlineData("oid")]
    public async Task ValidateIdTokenAsync_MissingClaim_Fails(string claimType)
    {
        var claims = TestIdentities.AccessTokenClaims().Without(claimType);
        var context = TokenContext(TestIdentities.Authenticated(claims));

        await WebTokenValidation.ValidateIdTokenAsync(context, TestIdentities.Settings);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
    }

    [Fact]
    public async Task RedirectRemoteFailureAsync_ErrorWithDescription_RedirectsToConstantErrorPage()
    {
        var httpContext = new DefaultHttpContext();
        var failure = new InvalidOperationException("access_denied: <script>alert(1)</script> AADSTS50020 user@example.test");
        var context = new RemoteFailureContext(
            httpContext,
            new AuthenticationScheme("OpenIdConnect", null, typeof(OpenIdConnectHandler)),
            new OpenIdConnectOptions(),
            failure);

        await WebTokenValidation.RedirectRemoteFailureAsync(context);

        Assert.Equal(StatusCodes.Status302Found, httpContext.Response.StatusCode);
        Assert.Equal("/account/sign-in-error", httpContext.Response.Headers.Location.ToString());
        Assert.True(context.Result.Handled);
    }

    [Fact]
    public async Task ValidateIdTokenAsync_NullArguments_Throw()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => WebTokenValidation.ValidateIdTokenAsync(null!, TestIdentities.Settings));
        await Assert.ThrowsAsync<ArgumentNullException>(() => WebTokenValidation.RedirectRemoteFailureAsync(null!));
    }
}
