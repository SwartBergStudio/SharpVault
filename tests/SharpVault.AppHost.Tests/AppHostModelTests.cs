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

    private static readonly string TestKeyMaterial = Convert.ToBase64String(RandomNumberGenerator.GetBytes(TestKeyMaterialByteCount));

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

        Assert.Equal(["api", "development-key-material", "storage", "tables", "web"], names);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task AppHost_Model_DeclaresOnlyTheDevelopmentKeyMaterialParameter()
    {
        var builder = await CreateBuilderAsync();

        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>());

        Assert.Equal(AppHostResourceNames.DevelopmentKeyMaterial, parameter.Name);
        Assert.Equal("development-key-material", parameter.Name);
        Assert.True(parameter.Secret);
        Assert.NotNull(parameter.Default);
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
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>());
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
        var parameter = Assert.Single(builder.Resources.OfType<ParameterResource>());
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

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync() =>
        await CreateBuilderAsync([]);

    private static async Task<IDistributedApplicationTestingBuilder> CreateBuilderAsync(string[] additionalArguments) =>
        await DistributedApplicationTestingBuilder.CreateAsync<Projects.SharpVault_AppHost>(
            [$"--Parameters:{AppHostResourceNames.DevelopmentKeyMaterial}={TestKeyMaterial}", .. additionalArguments],
            TestContext.Current.CancellationToken);

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
