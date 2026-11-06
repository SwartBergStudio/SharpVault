// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Identity;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Authentication.Mode;
using SharpVault.Infrastructure.Directory;

namespace SharpVault.Api.Security;

public static class UserDirectoryRegistration
{
    public static IHostApplicationBuilder AddUserDirectory(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var mode = builder.AddAuthenticationMode();
        if (mode.Mode == AuthenticationMode.Local)
        {
            builder.Services.AddSingleton<IUserDirectory, TableUserDirectory>();
            return builder;
        }

        builder.Services.AddSingleton(new GraphCredential(new DefaultAzureCredential(
            new DefaultAzureCredentialOptions { TenantId = mode.Entra.TenantId.ToString("D") })));
        builder.Services.AddHttpClient<IUserDirectory, GraphUserDirectory>();
        return builder;
    }
}
