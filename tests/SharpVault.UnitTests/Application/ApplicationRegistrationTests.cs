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
using SharpVault.Application;

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
    public void AddApplication_NoValidatorsDefined_RegistersNoValidators()
    {
        using var provider = BuildProvider();

        var validators = provider.GetServices<IValidator>();

        Assert.Empty(validators);
    }

    [Fact]
    public void AddApplication_Registered_ResolvesEverythingWithScopeValidation()
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

        Assert.Null(exception);
    }

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection().AddLogging().AddApplication().BuildServiceProvider();
}
