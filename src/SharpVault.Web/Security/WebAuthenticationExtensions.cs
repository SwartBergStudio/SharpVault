// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.TokenCacheProviders.Distributed;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UI.Shared.Navigation;
using SharpVault.Web.Api;

namespace SharpVault.Web.Security;

public static class WebAuthenticationExtensions
{
    private const string CookieName = "__Host-SharpVault";
    private const string CookiePath = "/";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);

    public static IHostApplicationBuilder AddWebAuthentication(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
        var entra = EntraApplicationSettings.Read(section);
        WebCredentialGuard.EnsureAllowed(section, builder.Environment);
        var scopes = builder.Configuration.GetSection(SharpVaultApiOptions.SectionName)
            .GetSection(nameof(SharpVaultApiOptions.Scopes)).Get<string[]>() ?? [];

        builder.Services.AddSingleton(entra);
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
        builder.Services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddCascadingAuthenticationState();
        return builder;
    }

    internal static void ConfigureOpenIdConnect(OpenIdConnectOptions options, EntraApplicationSettings entra)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = EntraClaimTypes.Name;
        options.TokenValidationParameters.RoleClaimType = EntraClaimTypes.Roles;
        options.TokenValidationParameters.ValidIssuers = [entra.V2Issuer];
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
        options.ExpireTimeSpan = SessionLifetime;
        options.SlidingExpiration = false;
        options.AccessDeniedPath = AccountRoutes.AccessDenied;
    }
}
