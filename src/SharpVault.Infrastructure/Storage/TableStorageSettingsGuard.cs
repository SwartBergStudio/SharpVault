// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Aspire.Azure.Data.Tables;
using Microsoft.Extensions.Hosting;

namespace SharpVault.Infrastructure.Storage;

public static class TableStorageSettingsGuard
{
    public static void EnsureAllowed(AzureDataTablesSettings settings, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(environment);

        var hasConnectionString = !string.IsNullOrWhiteSpace(settings.ConnectionString);

        if (!hasConnectionString && settings.ServiceUri is null)
        {
            throw new InvalidOperationException(TableStorageMessages.NotConfigured);
        }

        if (environment.IsDevelopment())
        {
            return;
        }

        if (hasConnectionString)
        {
            throw new InvalidOperationException(TableStorageMessages.ConnectionStringNotAllowed);
        }

        if (!IsAllowedServiceUri(settings.ServiceUri))
        {
            throw new InvalidOperationException(TableStorageMessages.ServiceUriNotAllowed);
        }
    }

    private static bool IsAllowedServiceUri(Uri? serviceUri) =>
        serviceUri is { IsAbsoluteUri: true } uri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Query.Length == 0
        && uri.UserInfo.Length == 0;
}
