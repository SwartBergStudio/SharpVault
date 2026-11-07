// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Behaviors;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.OneTimeLinks.Common;
using SharpVault.Application.Features.Sharing.Common;
using SharpVault.Application.Features.Tenancy;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Transfers.Job;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Users.Common;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Application.Features.Vault.RecycleBin;
using SharpVault.Application.Mapping;

namespace SharpVault.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.AddValidatorsFromAssembly(assembly);
        services.AddMediator(options => options.EnablePersistence = false, assembly);
        services.AddApplicationMappings();
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddSingleton<IUserAccessGate, UserAccessGate>();
        services.AddSingleton<IGuestAccessPolicy, ConfiguredGuestAccessPolicy>();
        services.AddSingleton<IOneTimeLinkPolicy, ConfiguredOneTimeLinkPolicy>();
        services.AddSingleton<IValidateOptions<UserAccessOptions>, UserAccessOptionsValidator>();
        services.AddSingleton<VaultKeyRingService>();
        services.AddScoped<VaultAuditWriter>();
        services.AddScoped<AdminActorGuard>();
        services.AddScoped<UserStatusWriter>();
        services.AddSingleton<AdminAuditWriter>();
        services.AddSingleton<OneTimeLinkAuditWriter>();
        services.AddSingleton<OneTimeLinkRevoker>();
        services.AddSingleton<IOneTimeLinkPurger, OneTimeLinkPurger>();
        services.AddScoped<ShareAccessResolver>();
        services.AddScoped<ShareManagementGate>();
        services.AddScoped<ISecretAccessPolicy, SecretAccessPolicy>();
        services.AddSingleton<ShareGrantService>();
        services.AddSingleton<ITransferJobSignal, TransferJobSignal>();
        services.AddSingleton<TransferAdmission>();
        services.AddSingleton<TransferGate>();
        services.AddSingleton<TransferPreflightPhase>();
        services.AddSingleton<TransferStagePhase>();
        services.AddSingleton<TransferReleasePhase>();
        services.AddSingleton<TransferPublishPhase>();
        services.AddSingleton<TransferReassignPhase>();
        services.AddSingleton<TransferCompletionPhase>();
        services.AddSingleton<TransferLifecyclePhase>();
        services.AddSingleton<ITransferJobRunner, TransferJobRunner>();
        services.AddOptions<RecycleBinPurgerOptions>();
        services.AddSingleton<RecycledItemEraser>();
        services.AddSingleton<IRecycleBinPurger, RecycleBinPurger>();

        return services;
    }
}
