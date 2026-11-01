// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace SharpVault.IntegrationTests.Authentication;

public static class TestIdentityProvider
{
    public const string Instance = "https://login.microsoftonline.com/";

    public static Uri InstanceUri { get; } = new(Instance);

    public static string Issuer(Guid tenantId) => $"{Instance}{tenantId:D}/v2.0";

    public static void ConfigureApi(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var configuration = new OpenIdConnectConfiguration { Issuer = Issuer(TestTenant.Id) };
            configuration.SigningKeys.Add(TestSigningKeys.Primary);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
        });
    }

    public static void ConfigureWeb(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
        {
            var tenantRoot = $"{Instance}{TestTenant.Id:D}/";
            var configuration = new OpenIdConnectConfiguration
            {
                Issuer = Issuer(TestTenant.Id),
                AuthorizationEndpoint = tenantRoot + "oauth2/v2.0/authorize",
                TokenEndpoint = tenantRoot + "oauth2/v2.0/token",
                EndSessionEndpoint = tenantRoot + "oauth2/v2.0/logout",
            };
            configuration.SigningKeys.Add(TestSigningKeys.Primary);
            options.Configuration = configuration;
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            options.Events.OnRedirectToIdentityProviderForSignOut = _ => Task.CompletedTask;
        });
    }

    public static void ApplyApiSettings(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("AzureAd:Instance", Instance);
        builder.UseSetting("AzureAd:TenantId", TestTenant.Id.ToString("D"));
        builder.UseSetting("AzureAd:ClientId", TestApi.ClientId.ToString("D"));
    }

    public static void ApplyWebSettings(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseSetting("AzureAd:Instance", Instance);
        builder.UseSetting("AzureAd:TenantId", TestTenant.Id.ToString("D"));
        builder.UseSetting("AzureAd:ClientId", TestWebApp.ClientId.ToString("D"));
        builder.UseSetting("SharpVaultApi:BaseAddress", "https://localhost/");
        builder.UseSetting("SharpVaultApi:Scopes:0", TestApi.ScopeUri);
    }
}
