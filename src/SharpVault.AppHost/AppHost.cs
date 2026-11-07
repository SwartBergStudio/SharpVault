// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.AppHost;

var builder = DistributedApplication.CreateBuilder(args);

var tables = builder.AddAzureStorage(AppHostResourceNames.Storage)
    .RunAsEmulator(emulator => emulator
        .WithDataVolume()
        .WithLifetime(ContainerLifetime.Persistent))
    .AddTables(AppHostResourceNames.Tables);

var api = builder.AddProject<Projects.SharpVault_Api>(AppHostResourceNames.Api)
    .WithReference(tables)
    .WaitFor(tables)
    .WithHttpHealthCheck(AppHostResourceNames.HealthPath);

if (builder.ExecutionContext.IsRunMode)
{
    var developmentKeyMaterial = builder.AddParameter(
        AppHostResourceNames.DevelopmentKeyMaterial,
        new GenerateParameterDefault { MinLength = AppHostResourceNames.DevelopmentKeyMaterialLength, Special = false },
        secret: true,
        persist: true);
    api.WithEnvironment(AppHostResourceNames.DevelopmentKeyMaterialVariable, developmentKeyMaterial);
}

var web = builder.AddProject<Projects.SharpVault_Web>(AppHostResourceNames.Web)
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck(AppHostResourceNames.HealthPath);

if (builder.ExecutionContext.IsRunMode)
{
    var webGatewayKey = builder.AddParameter(
        AppHostResourceNames.WebGatewayKey,
        new GenerateParameterDefault { MinLength = AppHostResourceNames.WebGatewayKeyLength, Special = false },
        secret: true,
        persist: true);
    api.WithEnvironment(AppHostResourceNames.WebGatewayKeyVariable, webGatewayKey);
    web.WithEnvironment(AppHostResourceNames.WebGatewayKeyVariable, webGatewayKey);
    api.WithEnvironment(AppHostResourceNames.OneTimeLinksEnabledVariable, AppHostResourceNames.OneTimeLinksEnabledValue);

    var tenantId = ConfiguredValue(AppHostResourceNames.TenantIdKey);
    var instance = ConfiguredValue(AppHostResourceNames.InstanceKey);
    var apiClientId = ConfiguredValue(AppHostResourceNames.ApiClientIdKey);
    var webClientId = ConfiguredValue(AppHostResourceNames.WebClientIdKey);

    foreach (var host in new[] { api, web })
    {
        if (tenantId is not null)
        {
            host.WithEnvironment(AppHostResourceNames.TenantIdVariable, tenantId);
        }

        if (instance is not null)
        {
            host.WithEnvironment(AppHostResourceNames.InstanceVariable, instance);
        }
    }

    if (apiClientId is not null)
    {
        api.WithEnvironment(AppHostResourceNames.ClientIdVariable, apiClientId);
        web.WithEnvironment(
            AppHostResourceNames.ApiScopeVariable,
            AppHostResourceNames.ApiScopePrefix + apiClientId + AppHostResourceNames.ApiScopeSuffix);
    }

    if (webClientId is not null)
    {
        web.WithEnvironment(AppHostResourceNames.ClientIdVariable, webClientId);
    }

    if (ConfiguredValue(AppHostResourceNames.WebClientSecretKey) is not null)
    {
        var webClientSecret = builder.AddParameterFromConfiguration(
            AppHostResourceNames.WebClientSecretParameter,
            AppHostResourceNames.WebClientSecretKey,
            secret: true);
        web.WithEnvironment(
            AppHostResourceNames.ClientCredentialSourceTypeVariable,
            AppHostResourceNames.ClientSecretSourceType);
        web.WithEnvironment(AppHostResourceNames.ClientCredentialSecretVariable, webClientSecret);
    }
}

await builder.Build().RunAsync();

string? ConfiguredValue(string key)
{
    var value = builder.Configuration[key];
    return string.IsNullOrWhiteSpace(value) ? null : value;
}
