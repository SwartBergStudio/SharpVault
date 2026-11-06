// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using SharpVault.Authentication.Identity;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

[Trait("Category", "Security")]
public sealed class WebAuthenticationExtensionsTests
{
    private const string ConfigureMethodName = "ConfigureOpenIdConnect";
    private const string OpenIdScheme = "OpenIdConnect";
    private const string ForgedSessionId = "forged-session-id";
    private const string ForgedExpiry = "99999999999";

    private static readonly DateTimeOffset SignedInAt = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfigureOpenIdConnect_TokenValidated_AddsSessionClaims()
    {
        var clock = new FakeTimeProvider(SignedInAt);
        var options = ConfiguredOptions();
        var principal = TestIdentities.Authenticated(TestIdentities.AccessTokenClaims());
        var context = TokenContext(principal, clock, options);

        await options.Events.OnTokenValidated(context);

        Assert.Null(context.Result);
        Assert.Single(principal.FindAll(SessionClaims.SessionId));
        Assert.NotEmpty(principal.FindFirst(SessionClaims.SessionId)!.Value);
        Assert.Equal(ExpectedExpiry(clock), principal.FindFirst(SessionClaims.ExpiresAt)?.Value);
    }

    [Fact]
    public async Task ConfigureOpenIdConnect_TokenValidatedWithCallerSuppliedClaims_ReplacesThem()
    {
        var clock = new FakeTimeProvider(SignedInAt);
        var options = ConfiguredOptions();
        var claims = TestIdentities.AccessTokenClaims()
            .With(SessionClaims.SessionId, ForgedSessionId)
            .With(SessionClaims.ExpiresAt, ForgedExpiry);
        var principal = TestIdentities.Authenticated(claims);
        var context = TokenContext(principal, clock, options);

        await options.Events.OnTokenValidated(context);

        Assert.Null(context.Result);
        var sessionIds = principal.FindAll(SessionClaims.SessionId).Select(claim => claim.Value).ToList();
        Assert.Single(sessionIds);
        Assert.DoesNotContain(ForgedSessionId, sessionIds, StringComparer.Ordinal);
        Assert.Equal(ExpectedExpiry(clock), principal.FindFirst(SessionClaims.ExpiresAt)?.Value);
    }

    [Fact]
    public async Task ConfigureOpenIdConnect_ValidationFailed_AddsNoClaims()
    {
        var clock = new FakeTimeProvider(SignedInAt);
        var options = ConfiguredOptions();
        var foreign = TestIdentities.OtherTenantId.ToString("D");
        var principal = TestIdentities.Authenticated(TestIdentities.AccessTokenClaims().Replacing(EntraClaimTypes.TenantId, foreign));
        var context = TokenContext(principal, clock, options);

        await options.Events.OnTokenValidated(context);

        Assert.NotNull(context.Result);
        Assert.False(context.Result.Succeeded);
        Assert.Empty(principal.FindAll(SessionClaims.SessionId));
        Assert.Empty(principal.FindAll(SessionClaims.ExpiresAt));
    }

    private static string ExpectedExpiry(FakeTimeProvider clock) =>
        clock.GetUtcNow().Add(WebApp::SharpVault.Web.Security.SessionLifetime.Value).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static OpenIdConnectOptions ConfiguredOptions()
    {
        var options = new OpenIdConnectOptions();
        var configure = typeof(WebApp::SharpVault.Web.Security.WebAuthenticationExtensions)
            .GetMethod(ConfigureMethodName, BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("ConfigureOpenIdConnect was not found.");
        configure.Invoke(null, [options, TestIdentities.Settings]);
        return options;
    }

    private static TokenValidatedContext TokenContext(ClaimsPrincipal principal, TimeProvider clock, OpenIdConnectOptions options)
    {
        var services = new ServiceCollection();
        services.AddSingleton(clock);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        return new TokenValidatedContext(
            httpContext,
            new AuthenticationScheme(OpenIdScheme, null, typeof(OpenIdConnectHandler)),
            options,
            principal,
            new AuthenticationProperties());
    }
}
