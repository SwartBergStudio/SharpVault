// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Identity.Web;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Tenancy;
using SharpVault.Application.Features.Users.Access;
using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public static class ApiAuthenticationExtensions
{
    public static IHostApplicationBuilder AddApiAuthentication(this IHostApplicationBuilder builder)
    {
        var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
        var entra = EntraApplicationSettings.Read(section);
        builder.Services.AddSingleton(entra);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(jwt => ConfigureJwtBearer(jwt, entra), identity => section.Bind(identity));
        builder.Services.AddOptions<UserAccessOptions>()
            .Bind(builder.Configuration.GetSection(UserAccessOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<TenancyOptions>()
            .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddSingleton<IAuthorizationHandler, SharpVaultAccessHandler>();

        var userPolicy = BuildPolicy(AppRoles.User, AppRoles.Admin);
        var adminPolicy = BuildPolicy(AppRoles.Admin);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.User, userPolicy)
            .AddPolicy(Policies.Admin, adminPolicy)
            .SetDefaultPolicy(userPolicy)
            .SetFallbackPolicy(userPolicy);
        return builder;
    }

    private static AuthorizationPolicy BuildPolicy(params string[] roles) =>
        new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
            .RequireAuthenticatedUser()
            .AddRequirements(new SharpVaultAccessRequirement(roles))
            .Build();

    private static void ConfigureJwtBearer(JwtBearerOptions jwt, EntraApplicationSettings entra)
    {
        jwt.MapInboundClaims = false;
        jwt.IncludeErrorDetails = false;
        jwt.TokenValidationParameters.NameClaimType = EntraClaimTypes.Name;
        jwt.TokenValidationParameters.RoleClaimType = EntraClaimTypes.Roles;
        jwt.TokenValidationParameters.ValidIssuers = [entra.V2Issuer];
        jwt.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
        jwt.TokenValidationParameters.ClockSkew = AccessTokenValidation.ClockSkew;
        jwt.Events ??= new JwtBearerEvents();
        jwt.Events.OnTokenValidated = context => AccessTokenValidation.ValidateAsync(context, entra);
    }
}
