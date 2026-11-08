// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mapster;
using MapsterMapper;
using Mediator;
using Mediator.Core;
using Mediator.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharpVault.Application;
using SharpVault.Application.Common.Behaviors;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Tenancy;
using SharpVault.Application.Features.Transfers.Common;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.UnitTests.Application;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void AddApplication_Registered_DisablesNotificationPersistence()
    {
        using var provider = BuildProvider();

        var options = provider.GetRequiredService<IOptions<MediatorOptions>>().Value;
        var persistence = provider.GetService<INotificationPersistence>();

        Assert.False(options.EnablePersistence);
        Assert.Null(persistence);
    }

    [Fact]
    public void AddApplication_Registered_RegistersFeatureValidatorsAndValidationBehavior()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<ListUsersQuery>));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(ValidationBehavior<,>));
    }

    [Fact]
    public void AddApplication_Registered_ResolvesAccessGateGuestPolicyAndOptionsValidator()
    {
        using var provider = BuildProviderWithHostServices();

        Assert.NotNull(provider.GetService<IUserAccessGate>());
        Assert.NotNull(provider.GetService<IGuestAccessPolicy>());
        Assert.NotNull(provider.GetService<IValidateOptions<UserAccessOptions>>());
    }

    [Fact]
    public void AddApplication_Registered_ResolvesEverythingWithScopeValidation()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();
        AddHostServices(services);

        var exception = Record.Exception(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
        });

        Assert.Null(exception);
    }

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection().AddLogging().AddApplication().BuildServiceProvider();

    private static ServiceProvider BuildProviderWithHostServices()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();
        AddHostServices(services);
        return services.BuildServiceProvider();
    }

    private static void AddHostServices(IServiceCollection services)
    {
        services.AddSingleton(Substitute.For<IUserStore>());
        services.AddSingleton(Substitute.For<IAuditLog>());
        services.AddSingleton(Substitute.For<IUserAccessCache>());
        services.AddSingleton(Substitute.For<IVaultStore>());
        services.AddSingleton(Substitute.For<IShareInboxStore>());
        services.AddSingleton(Substitute.For<IUserDirectory>());
        services.AddSingleton(Substitute.For<IMaintenanceLeaseStore>());
        services.AddSingleton(Substitute.For<ITransferJobStore>());
        services.AddSingleton(Substitute.For<ITransferStagingStore>());
        services.AddSingleton(Substitute.For<IOneTimeLinkStore>());
        services.AddSingleton(Substitute.For<ISessionRevocationLedger>());
        services.AddSingleton(Substitute.For<IEnvelopeEncryptor>());
        services.AddScoped(_ => Substitute.For<IRequestMetadata>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddOptions<UserAccessOptions>();
        services.AddOptions<TenancyOptions>();
        services.AddOptions<TransferOptions>();
    }
}
