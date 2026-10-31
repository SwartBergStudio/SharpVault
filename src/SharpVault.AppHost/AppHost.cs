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

builder.AddProject<Projects.SharpVault_Web>(AppHostResourceNames.Web)
    .WithExternalHttpEndpoints()
    .WithReference(api)
    .WaitFor(api)
    .WithHttpHealthCheck(AppHostResourceNames.HealthPath);

await builder.Build().RunAsync();
