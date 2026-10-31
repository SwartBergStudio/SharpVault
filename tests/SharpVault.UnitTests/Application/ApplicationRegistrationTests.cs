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
using SharpVault.Application.Features.Users.Access;
using SharpVault.Application.Features.Users.Queries;

namespace SharpVault.UnitTests.Application;

public sealed class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplication_Registered_ResolvesMediator()
    {
        using var provider = BuildProvider();

        var mediator = provider.GetService<IMediator>();

        Assert.NotNull(mediator);
    }

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
    public void AddApplication_Registered_ResolvesMapsterMapper()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var mapper = scope.ServiceProvider.GetService<IMapper>();

        Assert.NotNull(mapper);
    }

    [Fact]
    public void AddApplication_Registered_ResolvesTypeAdapterConfig()
    {
        using var provider = BuildProvider();

        var config = provider.GetService<TypeAdapterConfig>();

        Assert.NotNull(config);
    }

    [Fact]
    public void AddApplication_Registered_MapperIsScopedPerScope()
    {
        using var provider = BuildProvider();
        using var firstScope = provider.CreateScope();
        using var secondScope = provider.CreateScope();

        var first = firstScope.ServiceProvider.GetRequiredService<IMapper>();
        var sameScope = firstScope.ServiceProvider.GetRequiredService<IMapper>();
        var second = secondScope.ServiceProvider.GetRequiredService<IMapper>();

        Assert.Same(first, sameScope);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void AddApplication_Registered_ReturnsSameServiceCollection()
    {
        var services = new ServiceCollection();

        var returned = services.AddApplication();

        Assert.Same(services, returned);
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
    public void AddApplication_HostServicesMissing_FailsScopeValidation()
    {
        var services = new ServiceCollection().AddLogging().AddApplication();

        var exception = Record.Exception(() =>
        {
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true,
            });
        });

        Assert.IsType<AggregateException>(exception);
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
        services.AddSingleton(TimeProvider.System);
        services.AddScoped(_ => Substitute.For<ICurrentUser>());
        services.AddOptions<UserAccessOptions>();
        services.AddOptions<TenancyOptions>();
    }
}
