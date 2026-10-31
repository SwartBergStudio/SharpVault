// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Behaviors;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Tenancy;
using SharpVault.Application.Features.Users.Access;
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
        services.AddSingleton<IValidateOptions<UserAccessOptions>, UserAccessOptionsValidator>();

        return services;
    }
}
