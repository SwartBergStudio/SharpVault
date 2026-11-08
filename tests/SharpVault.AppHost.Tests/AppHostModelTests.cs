// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.AppHost.Tests;

public sealed class AppHostModelTests
{
    private const string StorageName = "storage";
    private const string AzureEnvironmentName = "azure-environment";
    private const string ApiName = "api";
    private const string WebName = "web";
    private const string AzuriteRegistry = "mcr.microsoft.com";
    private const string AzuriteImage = "azure-storage/azurite";
    private const string AzuriteTag = "3.35.0";
    private const string AzuriteDataPath = "/data";
    private const int TestKeyMaterialByteCount = 48;
    private const string TenantId = "5f0c1d2e-4b7a-4c3d-9e8f-00000000a001";
    private const string ApiClientId = "5f0c1d2e-4b7a-4c3d-9e8f-00000000a002";
    private const string WebClientId = "5f0c1d2e-4b7a-4c3d-9e8f-00000000a003";
    private const string Instance = "https://login.microsoftonline.com/";
    private const string WebSecret = "apphost-test-web-secret-value";

    private static readonly string TestKeyMaterial = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TestKeyMaterialByteCount));

    private static readonly string[] BlockedForwardedKeys =
    [
        $"--{AppHostResourceNames.TenantIdKey}=",
        $"--{AppHostResourceNames.InstanceKey}=",
        $"--{AppHostResourceNames.ApiClientIdKey}=",
        $"--{AppHostResourceNames.WebClientIdKey}=",
        $"--{AppHostResourceNames.WebClientSecretKey}=",
    ];

    [Fact]
    public async Task AppHost_StorageEmulator_UsesPersistentLifetime()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        Assert.True(storage.TryGetLastAnnotation<ContainerLifetimeAnnotation>(out var lifetime));
        Assert.Equal(ContainerLifetime.Persistent, lifetime.Lifetime);
    }

    [Fact]
    public async Task AppHost_StorageEmulator_MountsDataVolumeAtData()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        var mount = Assert.Single(storage.Annotations.OfType<ContainerMountAnnotation>());
        Assert.Equal(ContainerMountType.Volume, mount.Type);
        Assert.Equal(AzuriteDataPath, mount.Target);
        Assert.False(string.IsNullOrWhiteSpace(mount.Source));
        Assert.False(mount.IsReadOnly);
    }

    [Fact]
    public async Task AppHost_StorageEmulator_UsesNamedVolumeNotHostBindMount()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        var mount = Assert.Single(storage.Annotations.OfType<ContainerMountAnnotation>());
        Assert.False(Path.IsPathRooted(mount.Source));
        Assert.EndsWith("-storage-data", mount.Source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppHost_StorageEmulator_UsesAzuriteImage()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        Assert.True(storage.TryGetLastAnnotation<ContainerImageAnnotation>(out var image));
        Assert.Equal(AzuriteRegistry, image.Registry);
        Assert.Equal(AzuriteImage, image.Image);
    }

    [Fact]
    public async Task AppHost_StorageEmulator_UsesPinnedAzuriteTagMatchingTestContainers()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        Assert.True(storage.TryGetLastAnnotation<ContainerImageAnnotation>(out var image));
        Assert.Equal(AzuriteTag, image.Tag);
        Assert.NotEqual("latest", image.Tag);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_StorageEmulator_ExposesNoExternalEndpoint()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        var endpoints = storage.Annotations.OfType<EndpointAnnotation>().ToList();
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.False(endpoint.IsExternal));
    }

    [Fact]
    public async Task AppHost_TablesResource_NameMatchesInfrastructureConnectionName()
    {
        var builder = await CreateBuilderAsync();

        var tables = Find<AzureTableStorageResource>(builder, TableStorageConnection.Name);

        Assert.Equal(TableStorageConnection.Name, tables.Name);
        Assert.Equal(StorageName, tables.Parent.Name);
    }

    [Fact]
    public async Task AppHost_ApiProject_ReferencesTables()
    {
        var builder = await CreateBuilderAsync();

        var api = Find<ProjectResource>(builder, ApiName);

        Assert.Contains(
            api.Annotations.OfType<ResourceRelationshipAnnotation>(),
            relationship => relationship.Resource.Name == TableStorageConnection.Name && relationship.Type == "Reference");
    }

    [Fact]
    public async Task AppHost_ApiProject_WaitsForTables()
    {
        var builder = await CreateBuilderAsync();

        var api = Find<ProjectResource>(builder, ApiName);

        Assert.Contains(
            api.Annotations.OfType<WaitAnnotation>(),
            wait => wait.Resource.Name == TableStorageConnection.Name && wait.WaitType == WaitType.WaitUntilHealthy);
    }

    [Fact]
    public async Task AppHost_WebProject_WaitsForApi()
    {
        var builder = await CreateBuilderAsync();

        var web = Find<ProjectResource>(builder, WebName);

        Assert.Contains(
            web.Annotations.OfType<WaitAnnotation>(),
            wait => wait.Resource.Name == ApiName && wait.WaitType == WaitType.WaitUntilHealthy);
    }

    [Fact]
    public async Task AppHost_WebProject_ReferencesApi()
    {
        var builder = await CreateBuilderAsync();

        var web = Find<ProjectResource>(builder, WebName);

        Assert.Contains(
            web.Annotations.OfType<ResourceRelationshipAnnotation>(),
            relationship => relationship.Resource.Name == ApiName && relationship.Type == "Reference");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_WebProject_DoesNotReferenceOrWaitForStorage()
    {
        var builder = await CreateBuilderAsync();

        var web = Find<ProjectResource>(builder, WebName);

        var related = web.Annotations.OfType<ResourceRelationshipAnnotation>().Select(relationship => relationship.Resource.Name);
        var waited = web.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name);
        Assert.DoesNotContain(TableStorageConnection.Name, related);
        Assert.DoesNotContain(StorageName, related);
        Assert.DoesNotContain(TableStorageConnection.Name, waited);
        Assert.DoesNotContain(StorageName, waited);
    }

    [Fact]
    public async Task AppHost_ApiProject_DoesNotReferenceOrWaitForWeb()
    {
        var builder = await CreateBuilderAsync();

        var api = Find<ProjectResource>(builder, ApiName);

        var related = api.Annotations.OfType<ResourceRelationshipAnnotation>().Select(relationship => relationship.Resource.Name);
        var waited = api.Annotations.OfType<WaitAnnotation>().Select(wait => wait.Resource.Name);
        Assert.DoesNotContain(WebName, related);
        Assert.DoesNotContain(WebName, waited);
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_ProjectResource_HasHealthCheck(string resourceName)
    {
        var builder = await CreateBuilderAsync();

        var project = Find<ProjectResource>(builder, resourceName);

        Assert.Single(project.Annotations.OfType<HealthCheckAnnotation>());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_ApiProject_ExposesNoExternalEndpoint()
    {
        var builder = await CreateBuilderAsync();

        var api = Find<ProjectResource>(builder, ApiName);

        var endpoints = api.Annotations.OfType<EndpointAnnotation>().ToList();
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.False(endpoint.IsExternal));
    }

    [Fact]
    public async Task AppHost_WebProject_ExposesExternalHttpEndpoints()
    {
        var builder = await CreateBuilderAsync();

        var web = Find<ProjectResource>(builder, WebName);

        var endpoints = web.Annotations.OfType<EndpointAnnotation>().ToList();
        Assert.NotEmpty(endpoints);
        Assert.All(endpoints, endpoint => Assert.True(endpoint.IsExternal));
    }

    [Fact]
    public async Task AppHost_Model_ContainsExactlyTheExpectedVisibleResources()
    {
        var builder = await CreateBuilderAsync();

        var names = builder.Resources
            .Where(resource => !resource.Annotations.OfType<HiddenAnnotation>().Any())
            .Where(resource => resource.Name != AzureEnvironmentName)
            .Select(resource => resource.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["api", "development-key-material", "storage", "tables", "web", "web-gateway-key"], names);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_Model_DeclaresOnlyTheKeyMaterialParameters()
    {
        var builder = await CreateBuilderAsync();

        var parameters = builder.Resources.OfType<ParameterResource>().ToList();

        Assert.Equal(
            [AppHostResourceNames.DevelopmentKeyMaterial, AppHostResourceNames.WebGatewayKey],
            parameters.Select(parameter => parameter.Name).Order(StringComparer.Ordinal));
        Assert.All(parameters, parameter =>
        {
            Assert.True(parameter.Secret);
            Assert.NotNull(parameter.Default);
        });
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_PublishMode_DeclaresNoParameterResources()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var builder = await CreateBuilderAsync(
                ["--operation", "publish", "--publisher", "manifest", "--output-path", outputPath]);

            Assert.DoesNotContain(builder.Resources, resource => resource is ParameterResource);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_ApiProject_ReceivesDevelopmentKeyMaterialParameter()
    {
        var builder = await CreateBuilderAsync();
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == AppHostResourceNames.DevelopmentKeyMaterial);
        var api = Find<ProjectResource>(builder, ApiName);

        var variables = await RunEnvironmentCallbacksAsync(api);

        Assert.True(variables.ContainsKey(AppHostResourceNames.DevelopmentKeyMaterialVariable));
        Assert.Same(parameter, variables[AppHostResourceNames.DevelopmentKeyMaterialVariable]);
        Assert.Equal("Encryption__DevelopmentKeyMaterial", AppHostResourceNames.DevelopmentKeyMaterialVariable);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_ApiProject_SetsNoOtherEncryptionVariables()
    {
        var builder = await CreateBuilderAsync();
        var api = Find<ProjectResource>(builder, ApiName);

        var variables = await RunEnvironmentCallbacksAsync(api);

        var encryptionKeys = variables.Keys
            .Where(key => key.StartsWith("Encryption__", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal([AppHostResourceNames.DevelopmentKeyMaterialVariable], encryptionKeys);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_WebProject_ReceivesNoEncryptionSettings()
    {
        var builder = await CreateBuilderAsync();
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == AppHostResourceNames.DevelopmentKeyMaterial);
        var web = Find<ProjectResource>(builder, WebName);

        var variables = await RunEnvironmentCallbacksAsync(web);

        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("Encryption", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Values, value => ReferenceEquals(value, parameter));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("AccountKey")]
    [InlineData("SharedAccessSignature")]
    [InlineData("ConnectionString")]
    [InlineData("vault.azure.net")]
    [InlineData("KeyVaultKeyId")]
    public void AppHost_Source_ContainsNoKeysOrConnectionStrings(string forbidden)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SharpVault.AppHost", "AppHost.cs"));

        Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("secret: true")]
    [InlineData("persist: true")]
    [InlineData("Special = false")]
    [InlineData("IsRunMode")]
    public void AppHost_Source_GeneratesKeyMaterialAsPersistedSecretWithoutSpecialCharacters(string required)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SharpVault.AppHost", "AppHost.cs"));

        Assert.Contains(required, source, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AppHost_DevelopmentKeyMaterialLength_IsAtLeastSixtyFour()
    {
        Assert.True(AppHostResourceNames.DevelopmentKeyMaterialLength >= 64);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("AppHost.cs")]
    [InlineData("AppHostResourceNames.cs")]
    public void AppHost_SourceFiles_ContainNoComments(string fileName)
    {
        var source = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "src", "SharpVault.AppHost", fileName));

        var comments = source.Skip(2).Where(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToList();

        Assert.Empty(comments);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("UseLocalAccount")]
    [InlineData("Authentication__")]
    [InlineData("Authentication:")]
    [InlineData("LocalAccount")]
    public void AppHost_Source_DoesNotSetLocalAccountSettings(string forbidden)
    {
        foreach (var fileName in new[] { "AppHost.cs", "AppHostResourceNames.cs" })
        {
            var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SharpVault.AppHost", fileName));

            Assert.DoesNotContain(forbidden, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_ProjectResource_SetsNoAuthenticationVariables(string resourceName)
    {
        var builder = await CreateBuilderAsync();
        var project = Find<ProjectResource>(builder, resourceName);

        var variables = await RunEnvironmentCallbacksAsync(project);

        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Keys, key => key.Contains("LocalAccount", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_Model_RunsAsEmulatorOnlyInRunMode()
    {
        var builder = await CreateBuilderAsync();

        var storage = Find<AzureStorageResource>(builder, StorageName);

        Assert.True(storage.IsEmulator);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SharpVault.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root could not be located.");
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task RunMode_GivesBothHostsTheSameWebGatewayKey()
    {
        var builder = await CreateBuilderAsync();
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), resource => resource.Name == AppHostResourceNames.WebGatewayKey);
        var api = Find<ProjectResource>(builder, ApiName);
        var web = Find<ProjectResource>(builder, WebName);

        var apiVariables = await RunEnvironmentCallbacksAsync(api);
        var webVariables = await RunEnvironmentCallbacksAsync(web);

        Assert.Same(parameter, apiVariables[AppHostResourceNames.WebGatewayKeyVariable]);
        Assert.Same(parameter, webVariables[AppHostResourceNames.WebGatewayKeyVariable]);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task PublishMode_SetsNoWebGatewayKey()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var builder = await CreateBuilderAsync(
                ["--operation", "publish", "--publisher", "manifest", "--output-path", outputPath]);
            var api = Find<ProjectResource>(builder, ApiName);
            var web = Find<ProjectResource>(builder, WebName);

            var apiVariables = await RunEnvironmentCallbacksAsync(api);
            var webVariables = await RunEnvironmentCallbacksAsync(web);

            Assert.False(apiVariables.ContainsKey(AppHostResourceNames.WebGatewayKeyVariable));
            Assert.False(webVariables.ContainsKey(AppHostResourceNames.WebGatewayKeyVariable));
            Assert.DoesNotContain(builder.Resources, resource => resource is ParameterResource parameter && parameter.Name == AppHostResourceNames.WebGatewayKey);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync() =>
        await CreateBuilderAsync([]);

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(string[] additionalArguments) =>
        await DistributedApplicationTestingBuilder.CreateAsync<Projects.SharpVault_AppHost>(
            [
                $"--Parameters:{AppHostResourceNames.DevelopmentKeyMaterial}={TestKeyMaterial}",
                .. BlockedForwardedKeys,
                .. additionalArguments,
            ],
            TestContext.Current.CancellationToken);

    private static string[] Configured(params (string Key, string Value)[] settings) =>
        [.. settings.Select(setting => $"--{setting.Key}={setting.Value}")];

    private static async Task<Dictionary<string, object>> VariablesOfAsync(string resourceName, params (string Key, string Value)[] settings)
    {
        var builder = await CreateBuilderAsync(Configured(settings));
        return await RunEnvironmentCallbacksAsync(Find<ProjectResource>(builder, resourceName));
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    [Trait("Category", "Security")]
    public async Task AppHost_NothingConfigured_ForwardsNoAzureAdVariables(string resourceName)
    {
        var variables = await VariablesOfAsync(resourceName);

        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("AzureAd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("SharpVaultApi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AppHost_NothingConfigured_ModelHasNoExtraResources()
    {
        var builder = await CreateBuilderAsync();

        Assert.DoesNotContain(builder.Resources, resource => resource.Name == AppHostResourceNames.WebClientSecretParameter);
        Assert.Equal(
            [AppHostResourceNames.DevelopmentKeyMaterial, AppHostResourceNames.WebGatewayKey],
            builder.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name).Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_TenantConfigured_ForwardsTenantToApiAndWeb(string resourceName)
    {
        var variables = await VariablesOfAsync(resourceName, (AppHostResourceNames.TenantIdKey, TenantId));

        Assert.Equal(TenantId, variables[AppHostResourceNames.TenantIdVariable]);
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_InstanceConfigured_ForwardsInstanceToApiAndWeb(string resourceName)
    {
        var variables = await VariablesOfAsync(resourceName, (AppHostResourceNames.InstanceKey, Instance));

        Assert.Equal(Instance, variables[AppHostResourceNames.InstanceVariable]);
    }

    [Fact]
    public async Task AppHost_ApiClientIdConfigured_ApiGetsItAsClientId()
    {
        var variables = await VariablesOfAsync(ApiName, (AppHostResourceNames.ApiClientIdKey, ApiClientId));

        Assert.Equal(ApiClientId, variables[AppHostResourceNames.ClientIdVariable]);
    }

    [Fact]
    public async Task AppHost_WebClientIdConfigured_WebGetsItAsClientId()
    {
        var variables = await VariablesOfAsync(WebName, (AppHostResourceNames.WebClientIdKey, WebClientId));

        Assert.Equal(WebClientId, variables[AppHostResourceNames.ClientIdVariable]);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_ApiClientIdConfigured_ApiDoesNotReceiveWebClientId()
    {
        var variables = await VariablesOfAsync(
            ApiName,
            (AppHostResourceNames.ApiClientIdKey, ApiClientId),
            (AppHostResourceNames.WebClientIdKey, WebClientId));

        Assert.Equal(ApiClientId, variables[AppHostResourceNames.ClientIdVariable]);
        Assert.DoesNotContain(variables.Values, value => Equals(value, WebClientId));
    }

    [Fact]
    public async Task AppHost_WebClientIdConfigured_WebDoesNotReceiveApiClientIdAsItsClientId()
    {
        var variables = await VariablesOfAsync(
            WebName,
            (AppHostResourceNames.ApiClientIdKey, ApiClientId),
            (AppHostResourceNames.WebClientIdKey, WebClientId));

        Assert.Equal(WebClientId, variables[AppHostResourceNames.ClientIdVariable]);
    }

    [Fact]
    public async Task AppHost_ApiClientIdConfigured_WebGetsDerivedScope()
    {
        var variables = await VariablesOfAsync(WebName, (AppHostResourceNames.ApiClientIdKey, ApiClientId));

        Assert.Equal($"api://{ApiClientId}/access_as_user", variables[AppHostResourceNames.ApiScopeVariable]);
        Assert.False(variables.ContainsKey(AppHostResourceNames.ClientIdVariable));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_WebSecretConfigured_AddsSecretParameterAndForwardsOnlyToWeb()
    {
        var builder = await CreateBuilderAsync(Configured((AppHostResourceNames.WebClientSecretKey, WebSecret)));
        var web = Find<ProjectResource>(builder, WebName);

        var variables = await RunEnvironmentCallbacksAsync(web);

        var secret = Assert.Single(builder.Resources.OfType<ParameterResource>(), parameter => parameter.Name == AppHostResourceNames.WebClientSecretParameter);
        Assert.True(secret.Secret);
        Assert.Same(secret, variables[AppHostResourceNames.ClientCredentialSecretVariable]);
        Assert.Equal(AppHostResourceNames.ClientSecretSourceType, variables[AppHostResourceNames.ClientCredentialSourceTypeVariable]);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_WebSecretConfigured_ApiReceivesNoSecretOrCredentialVariables()
    {
        var variables = await VariablesOfAsync(ApiName, (AppHostResourceNames.WebClientSecretKey, WebSecret));

        Assert.DoesNotContain(variables.Keys, key => key.Contains("ClientCredentials", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Keys, key => key.Contains("Secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Values, value => value is ParameterResource parameter && parameter.Name == AppHostResourceNames.WebClientSecretParameter);
        Assert.DoesNotContain(variables.Values, value => Equals(value, WebSecret));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_WebSecretConfigured_SecretIsAParameterResourceNotAPlainString()
    {
        var variables = await VariablesOfAsync(WebName, (AppHostResourceNames.WebClientSecretKey, WebSecret));

        var value = variables[AppHostResourceNames.ClientCredentialSecretVariable];
        Assert.IsType<ParameterResource>(value);
        Assert.DoesNotContain(variables.Values, candidate => Equals(candidate, WebSecret));
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_WhitespaceValues_AreNotForwarded(string resourceName)
    {
        var variables = await VariablesOfAsync(
            resourceName,
            (AppHostResourceNames.TenantIdKey, "   "),
            (AppHostResourceNames.InstanceKey, " "),
            (AppHostResourceNames.ApiClientIdKey, "  "),
            (AppHostResourceNames.WebClientIdKey, " "),
            (AppHostResourceNames.WebClientSecretKey, "   "));

        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("AzureAd", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("SharpVaultApi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AppHost_WhitespaceSecret_AddsNoParameter()
    {
        var builder = await CreateBuilderAsync(Configured((AppHostResourceNames.WebClientSecretKey, "  ")));

        Assert.DoesNotContain(builder.Resources, resource => resource.Name == AppHostResourceNames.WebClientSecretParameter);
    }

    [Theory]
    [InlineData(ApiName)]
    [InlineData(WebName)]
    public async Task AppHost_OnlyTenantConfigured_ForwardsOnlyTenant(string resourceName)
    {
        var variables = await VariablesOfAsync(resourceName, (AppHostResourceNames.TenantIdKey, TenantId));

        var azureAd = variables.Keys.Where(key => key.StartsWith("AzureAd", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Equal([AppHostResourceNames.TenantIdVariable], azureAd);
        Assert.DoesNotContain(variables.Keys, key => key.StartsWith("SharpVaultApi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_PublishMode_ForwardsNoAzureAdValuesAndDeclaresNoParameters()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var builder = await CreateBuilderAsync(
            [
                "--operation", "publish", "--publisher", "manifest", "--output-path", outputPath,
                .. Configured(
                    (AppHostResourceNames.TenantIdKey, TenantId),
                    (AppHostResourceNames.ApiClientIdKey, ApiClientId),
                    (AppHostResourceNames.WebClientIdKey, WebClientId),
                    (AppHostResourceNames.WebClientSecretKey, WebSecret)),
            ]);

            Assert.DoesNotContain(builder.Resources, resource => resource is ParameterResource);
            foreach (var name in new[] { ApiName, WebName })
            {
                var variables = await RunEnvironmentCallbacksAsync(Find<ProjectResource>(builder, name));
                Assert.DoesNotContain(variables.Keys, key => key.StartsWith("AzureAd", StringComparison.OrdinalIgnoreCase));
            }
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, recursive: true);
            }
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_DevelopmentKeyMaterial_StillGeneratedPersistedAndSecret()
    {
        var builder = await CreateBuilderAsync(Configured((AppHostResourceNames.WebClientSecretKey, WebSecret)));

        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>(), candidate => candidate.Name == AppHostResourceNames.DevelopmentKeyMaterial);

        Assert.True(parameter.Secret);
        Assert.NotNull(parameter.Default);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("ClientSecret = \"")]
    [InlineData("ClientSecret=\"")]
    [InlineData("secret: false")]
    public void AppHost_Source_ContainsNoClientSecretLiteral(string forbidden)
    {
        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "SharpVault.AppHost", "AppHost.cs"));

        Assert.DoesNotContain(forbidden, source, StringComparison.Ordinal);
        Assert.Contains("secret: true", source, StringComparison.Ordinal);
    }


    private static async Task<Dictionary<string, object>> RunEnvironmentCallbacksAsync(ProjectResource resource)
    {
        var context = new EnvironmentCallbackContext(
            new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run),
            resource,
            cancellationToken: TestContext.Current.CancellationToken);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    private static T Find<T>(IDistributedApplicationTestingBuilder builder, string name)
        where T : IResource
    {
        var resource = builder.Resources.SingleOrDefault(candidate => candidate.Name == name);
        Assert.NotNull(resource);
        return Assert.IsAssignableFrom<T>(resource);
    }
}
