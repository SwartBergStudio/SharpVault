// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Aspire.Azure.Data.Tables;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Identity;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure;

public static class DependencyInjection
{
    public static IHostApplicationBuilder AddInfrastructure(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddAzureTableServiceClient(
            TableStorageConnection.Name,
            settings => ConfigureTableStorageSettings(settings, builder.Environment),
            clientBuilder => clientBuilder.ConfigureOptions(AddRegisteredPipelinePolicies));
        builder.Services.AddSingleton(new TableStorageTableSet(TableNames.All));
        builder.Services.AddHostedService<TableStorageInitializer>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.AddEncryption();

        return builder;
    }

    private static void ConfigureTableStorageSettings(AzureDataTablesSettings settings, IHostEnvironment environment)
    {
        TableStorageSettingsGuard.EnsureAllowed(settings, environment);
        settings.Credential ??= new DefaultAzureCredential();
    }

    private static void AddRegisteredPipelinePolicies(TableClientOptions options, IServiceProvider services)
    {
        foreach (var policy in services.GetServices<HttpPipelinePolicy>())
        {
            options.AddPolicy(policy, HttpPipelinePosition.PerCall);
        }
    }
}
