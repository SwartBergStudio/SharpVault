// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SharpVault.Authentication.WebGateway;

public static class WebGatewayRegistration
{
    public static IHostApplicationBuilder AddWebGateway(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddOptions<WebGatewayOptions>()
            .Bind(builder.Configuration.GetSection(WebGatewayOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<WebGatewayOptions>>(
            new WebGatewayOptionsValidator(keyRequired: !builder.Environment.IsDevelopment()));
        return builder;
    }
}
