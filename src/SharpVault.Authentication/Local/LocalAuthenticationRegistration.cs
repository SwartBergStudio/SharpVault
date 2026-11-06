// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SharpVault.Authentication.Local;

public static class LocalAuthenticationRegistration
{
    public static TBuilder AddLocalAuthentication<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LocalAuthenticationOptions();
        builder.Configuration.GetSection(LocalAuthenticationOptions.SectionName).Bind(options);
        var path = string.IsNullOrWhiteSpace(options.KeyFilePath) ? LocalSigningKeyStore.DefaultPath() : options.KeyFilePath;
        var key = LocalSigningKeyStore.LoadOrCreate(path);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton(key);
        builder.Services.TryAddSingleton<ILocalTokenIssuer, LocalTokenIssuer>();
        builder.Services.AddHostedService<LocalAuthenticationStartupNotice>();
        return builder;
    }
}
