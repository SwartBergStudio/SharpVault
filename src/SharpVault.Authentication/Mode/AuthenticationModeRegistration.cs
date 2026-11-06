// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;

namespace SharpVault.Authentication.Mode;

public static class AuthenticationModeRegistration
{
    public static AuthenticationModeInfo AddAuthenticationMode(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var existing = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(AuthenticationModeInfo))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<AuthenticationModeInfo>()
            .FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var mode = AuthenticationModeResolver.Resolve(builder.Configuration, builder.Environment);
        var settings = mode == AuthenticationMode.Entra
            ? EntraApplicationSettings.Read(builder.Configuration.GetSection(EntraApplicationSettings.SectionName))
            : LocalAuthenticationDefaults.EntraSettings;

        var info = new AuthenticationModeInfo(mode, settings);
        builder.Services.AddSingleton(info);
        builder.Services.TryAddSingleton(settings);
        return info;
    }
}
