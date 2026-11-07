// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.Authentication.Mode;
using SharpVault.Authentication.WebGateway;
using SharpVault.UI.Shared;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.Web.Security;

namespace SharpVault.Web.Api;

public static class SharpVaultApiRegistration
{
    private const string EntraHomeButtonText = "Sign in with your work account";

    public static IHostApplicationBuilder AddSharpVaultApiClient(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mode = builder.AddAuthenticationMode();
        var local = mode.Mode == AuthenticationMode.Local;
        builder.Services.AddOptions<SharpVaultApiOptions>()
            .Bind(builder.Configuration.GetSection(SharpVaultApiOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SharpVaultApiOptions>>(
            new SharpVaultApiOptionsValidator(requireScopes: !local));
        if (local)
        {
            builder.Services.AddScoped<IApiAccessTokenProvider, LocalApiAccessTokenProvider>();
        }
        else
        {
            builder.Services.AddScoped<IApiAccessTokenProvider, EntraApiAccessTokenProvider>();
        }

        builder.AddWebGateway();
        builder.Services.AddTransient<WebGatewayProofHandler>();
        builder.Services.AddHttpClient<ISharedSessionClient, ApiSharedSessionClient>(
            (services, client) => client.BaseAddress = services.GetRequiredService<IOptions<SharpVaultApiOptions>>().Value.BaseAddress)
            .AddHttpMessageHandler<WebGatewayProofHandler>();
        builder.Services.AddSingleton<ISessionActions, WebSessionActions>();
        builder.Services.Configure<SignInPresentationOptions>(
            options => options.HomeButtonText = local ? SignInPresentationOptions.DefaultHomeButtonText : EntraHomeButtonText);
        builder.Services.AddSharpVaultUi(
            (services, client) => client.BaseAddress = services.GetRequiredService<IOptions<SharpVaultApiOptions>>().Value.BaseAddress,
            http => http.AddHttpMessageHandler<WebGatewayProofHandler>());
        return builder;
    }
}
