// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Identity.Web;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Tenancy;
using SharpVault.Application.Features.Users.Access;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.Mode;
using SharpVault.Authentication.WebGateway;

namespace SharpVault.Api.Security;

public static class ApiAuthenticationExtensions
{
    public static IHostApplicationBuilder AddApiAuthentication(this IHostApplicationBuilder builder)
    {
        var mode = builder.AddAuthenticationMode();
        if (mode.Mode == AuthenticationMode.Local)
        {
            return AddLocalAuthenticationScheme(builder, mode.Entra);
        }

        var section = builder.Configuration.GetSection(EntraApplicationSettings.SectionName);
        var entra = mode.Entra;
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(jwt => ConfigureJwtBearer(jwt, entra), identity => section.Bind(identity));
        builder.Services.PostConfigure<JwtBearerOptions>(
            JwtBearerDefaults.AuthenticationScheme,
            jwt => jwt.TokenValidationParameters.IssuerValidator = ExactIssuerValidator.For(entra.V2Issuer));
        return AddAccessControl(builder, entra, JwtBearerDefaults.AuthenticationScheme);
    }

    private static IHostApplicationBuilder AddLocalAuthenticationScheme(
        IHostApplicationBuilder builder,
        EntraApplicationSettings entra)
    {
        builder.AddLocalAuthentication();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<LocalSigningKey>((jwt, key) =>
            {
                jwt.MapInboundClaims = false;
                jwt.IncludeErrorDetails = false;
                jwt.TokenValidationParameters = LocalTokenValidation.Create(key);
                jwt.Events ??= new JwtBearerEvents();
                jwt.Events.OnTokenValidated = context => AccessTokenValidation.ValidateAsync(context, entra);
            });
        builder.Services.AddHostedService<LocalAccountSeeder>();
        return AddAccessControl(builder, entra, JwtBearerDefaults.AuthenticationScheme);
    }

    private static IHostApplicationBuilder AddAccessControl(
        IHostApplicationBuilder builder,
        EntraApplicationSettings entra,
        string scheme)
    {
        builder.Services.AddOptions<UserAccessOptions>()
            .Bind(builder.Configuration.GetSection(UserAccessOptions.SectionName))
            .ValidateOnStart();
        builder.Services.AddOptions<TenancyOptions>()
            .Bind(builder.Configuration.GetSection(TenancyOptions.SectionName))
            .ValidateOnStart();
        builder.AddWebGateway();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<ClientAddressResolver>();
        builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
        builder.Services.AddScoped<IRequestMetadata, HttpContextRequestMetadata>();
        builder.Services.AddSingleton<IAuthorizationHandler, SharpVaultAccessHandler>();

        var userPolicy = BuildPolicy(scheme, AppRoles.User, AppRoles.Admin);
        var adminPolicy = BuildPolicy(scheme, AppRoles.Admin);
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy(Policies.User, userPolicy)
            .AddPolicy(Policies.Admin, adminPolicy)
            .SetDefaultPolicy(userPolicy)
            .SetFallbackPolicy(userPolicy);
        return builder;
    }

    private static AuthorizationPolicy BuildPolicy(string scheme, params string[] roles) =>
        new AuthorizationPolicyBuilder(scheme)
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
