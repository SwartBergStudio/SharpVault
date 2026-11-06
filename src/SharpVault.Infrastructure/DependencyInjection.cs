// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Aspire.Azure.Data.Tables;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Data.Tables;
using Azure.Identity;
using Mapster;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Infrastructure.Audit;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Maintenance;
using SharpVault.Infrastructure.Sharing;
using SharpVault.Infrastructure.Storage;
using SharpVault.Infrastructure.Transfers;
using SharpVault.Infrastructure.Users;
using SharpVault.Infrastructure.Vaults;

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
        TypeAdapterConfig.GlobalSettings.Scan(typeof(DependencyInjection).Assembly);
        builder.Services.AddSingleton<IUserStore, TableUserStore>();
        builder.Services.AddSingleton<IAuditLog, TableAuditLog>();
        builder.Services.AddSingleton<IUserAccessCache, UserAccessCache>();
        builder.Services.AddSingleton<IVaultStore, TableVaultStore>();
        builder.Services.AddSingleton<IShareInboxStore, TableShareInboxStore>();
        builder.Services.AddSingleton<IMaintenanceLeaseStore, TableMaintenanceLeaseStore>();
        builder.Services.AddSingleton<ITransferJobStore, TableTransferJobStore>();
        builder.Services.AddSingleton<ITransferStagingStore, TableTransferStagingStore>();
        builder.Services.AddSingleton<ISessionRevocationLedger, TableSessionRevocationLedger>();
        builder.Services.AddHostedService<SessionRevocationPurgeService>();
        builder.Services.AddOptions<RecycleBinPurgeOptions>()
            .Bind(builder.Configuration.GetSection(RecycleBinPurgeOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<RecycleBinPurgeOptions>, RecycleBinPurgeOptionsValidator>();
        builder.Services.AddOptions<RecycleBinPurgerOptions>()
            .Configure<IOptions<RecycleBinPurgeOptions>>((target, source) => target.LeaseDuration = source.Value.LeaseDuration);
        builder.Services.AddHostedService<RecycleBinPurgeService>();
        builder.Services.AddOptions<TransferOptions>()
            .Bind(builder.Configuration.GetSection(TransferOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<TransferOptions>, TransferOptionsValidator>();
        builder.Services.AddHostedService<TransferJobService>();
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
