// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.Mode;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.Web.Api;

namespace SharpVault.Web.Security;

public static class WebAuthenticationExtensions
{
    private const string CookieName = "__Host-SharpVault";
    private const string CookiePath = "/";
    public static IHostApplicationBuilder AddWebAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mode = builder.AddAuthenticationMode();
        AddSessionServices(builder);
        if (mode.Mode == AuthenticationMode.Local)
        {
            return AddLocalSignIn(builder);
        }

        var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
        var entra = mode.Entra;
        WebCredentialGuard.EnsureAllowed(section, builder.Environment);
        var scopes = builder.Configuration.GetSection(SharpVaultApiOptions.SectionName)
            .GetSection(nameof(SharpVaultApiOptions.Scopes)).Get<string[]>() ?? [];

        builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(section)
            .EnableTokenAcquisitionToCallDownstreamApi(scopes)
            .AddDistributedTokenCaches();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.Configure<MsalDistributedTokenCacheAdapterOptions>(options => options.Encrypt = true);
        builder.Services.PostConfigure<OpenIdConnectOptions>(
            OpenIdConnectDefaults.AuthenticationScheme,
            options => ConfigureOpenIdConnect(options, entra));
        builder.Services.PostConfigure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ConfigureCookie);
        return AddAuthorizationServices(builder);
    }

    private static void AddSessionServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddDistributedMemoryCache();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISessionRevocationStore, DistributedSessionRevocationStore>();
        builder.Services.AddSingleton<ISessionValidator, SessionValidator>();
        builder.Services.AddSingleton<SessionActivityMap>();
        builder.Services.AddScoped<CircuitActivityTracker>();
        builder.Services.AddScoped<CircuitHandler, CircuitActivityHandler>();
        builder.Services.AddScoped<SessionRevalidator>();
        builder.Services.AddScoped<AuthenticationStateProvider, RevalidatingSessionAuthenticationStateProvider>();
    }

    private static IHostApplicationBuilder AddLocalSignIn(IHostApplicationBuilder builder)
    {
        builder.AddLocalAuthentication();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = AccountRoutes.SignIn;
                options.AccessDeniedPath = AccountRoutes.AccessDenied;
                options.ReturnUrlParameter = AccountRoutes.ReturnUrlParameter;
            });
        builder.Services.PostConfigure<CookieAuthenticationOptions>(
            CookieAuthenticationDefaults.AuthenticationScheme,
            ConfigureCookie);
        return AddAuthorizationServices(builder);
    }

    private static IHostApplicationBuilder AddAuthorizationServices(IHostApplicationBuilder builder)
    {
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddCascadingAuthenticationState();
        return builder;
    }

    internal static void ConfigureOpenIdConnect(OpenIdConnectOptions options, EntraApplicationSettings entra)
    {
        options.MapInboundClaims = false;
        if (options.TokenHandler is JsonWebTokenHandler tokenHandler)
        {
            tokenHandler.MapInboundClaims = false;
        }

        options.TokenValidationParameters.NameClaimType = EntraClaimTypes.Name;
        options.TokenValidationParameters.RoleClaimType = EntraClaimTypes.Roles;
        options.TokenValidationParameters.ValidIssuers = [entra.V2Issuer];
        options.TokenValidationParameters.IssuerValidator = ExactIssuerValidator.For(entra.V2Issuer);
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.UsePkce = true;
        options.SaveTokens = false;

        var previousValidated = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await previousValidated(context);
            if (context.Result is null)
            {
                await WebTokenValidation.ValidateIdTokenAsync(context, entra);
            }

            if (context.Result is null && context.Principal?.Identity is ClaimsIdentity identity)
            {
                SessionClaimsIssuer.Add(identity, context.HttpContext.RequestServices.GetRequiredService<TimeProvider>());
            }
        };

        var previousFailure = options.Events.OnRemoteFailure;
        options.Events.OnRemoteFailure = async context =>
        {
            await previousFailure(context);
            if (context.Result?.Handled != true)
            {
                await WebTokenValidation.RedirectRemoteFailureAsync(context);
            }
        };
    }

    internal static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = CookiePath;
        options.ExpireTimeSpan = SessionLifetime.Value;
        options.SlidingExpiration = false;
        options.AccessDeniedPath = AccountRoutes.AccessDenied;
    }
}
