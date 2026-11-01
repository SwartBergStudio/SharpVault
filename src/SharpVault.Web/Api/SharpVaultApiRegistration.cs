// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Options;
using SharpVault.UI.Shared.Services;

namespace SharpVault.Web.Api;

public static class SharpVaultApiRegistration
{
    public static IHostApplicationBuilder AddSharpVaultApiClient(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<SharpVaultApiOptions>()
            .Bind(builder.Configuration.GetSection(SharpVaultApiOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<SharpVaultApiOptions>, SharpVaultApiOptionsValidator>();
        builder.Services.AddHttpClient<ISharpVaultApiClient, SharpVaultApiClient>(
            (services, client) => client.BaseAddress = services.GetRequiredService<IOptions<SharpVaultApiOptions>>().Value.BaseAddress);
        return builder;
    }
}
