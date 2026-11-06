// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace SharpVault.Authentication.Mode;

public static class AuthenticationModeResolver
{
    public static AuthenticationMode Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var hasTenant = !string.IsNullOrWhiteSpace(configuration[AuthenticationModeMessages.TenantIdKey]);
        var hasClient = !string.IsNullOrWhiteSpace(configuration[AuthenticationModeMessages.ClientIdKey]);

        if (hasTenant && hasClient)
        {
            return AuthenticationMode.Entra;
        }

        if (hasTenant)
        {
            throw new InvalidOperationException(AuthenticationModeMessages.ClientIdMissing);
        }

        if (hasClient)
        {
            throw new InvalidOperationException(AuthenticationModeMessages.TenantIdMissing);
        }

        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(AuthenticationModeMessages.EntraRequiredOutsideDevelopment);
        }

        return AuthenticationMode.Local;
    }
}
