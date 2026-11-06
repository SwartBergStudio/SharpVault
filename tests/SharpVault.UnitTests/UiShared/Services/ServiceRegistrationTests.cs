// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using NSubstitute;
using SharpVault.UI.Shared;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;
using SharpVault.UI.Shared.Services.Users;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Services;

public sealed class ServiceRegistrationTests
{
    private static readonly Uri BaseAddress = new("https://api.example.test/");

    private static readonly Type[] ClientTypes =
    [
        typeof(IMeApiClient),
        typeof(IAdminUsersApiClient),
        typeof(IVaultSecretsApiClient),
        typeof(ISecretHistoryApiClient),
        typeof(IVaultFoldersApiClient),
        typeof(IRecycleBinApiClient),
        typeof(ISharedItemsApiClient),
        typeof(IShareGrantsApiClient),
        typeof(IDirectoryApiClient),
    ];

    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IJSRuntime>());
        services.AddScoped(_ => Substitute.For<NavigationManager>());
        services.AddScoped(_ => Substitute.For<IApiAccessTokenProvider>());
        services.AddScoped(_ => Substitute.For<AuthenticationStateProvider>());
        services.AddSingleton(Substitute.For<ISessionValidator>());
        services.AddSharpVaultUi((_, client) => client.BaseAddress = BaseAddress);
        return services;
    }

    [Fact]
    public void AddSharpVaultUi_Clients_ResolveFromScopeWithScopedDependencies()
    {
        using var provider = Registered().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        var me = scope.ServiceProvider.GetRequiredService<IMeApiClient>();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminUsersApiClient>();
        var accessor = scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();

        Assert.IsType<MeApiClient>(me);
        Assert.IsType<AdminUsersApiClient>(admin);
        Assert.IsType<CurrentUserAccessor>(accessor);
        Assert.IsType<VaultSecretsApiClient>(scope.ServiceProvider.GetRequiredService<IVaultSecretsApiClient>());
        Assert.IsType<SecretHistoryApiClient>(scope.ServiceProvider.GetRequiredService<ISecretHistoryApiClient>());
        Assert.IsType<VaultFoldersApiClient>(scope.ServiceProvider.GetRequiredService<IVaultFoldersApiClient>());
        Assert.IsType<RecycleBinApiClient>(scope.ServiceProvider.GetRequiredService<IRecycleBinApiClient>());
        Assert.IsType<SharedItemsApiClient>(scope.ServiceProvider.GetRequiredService<ISharedItemsApiClient>());
        Assert.IsType<ShareGrantsApiClient>(scope.ServiceProvider.GetRequiredService<IShareGrantsApiClient>());
        Assert.IsType<DirectoryApiClient>(scope.ServiceProvider.GetRequiredService<IDirectoryApiClient>());
    }

    [Fact]
    public void AddSharpVaultUi_Clients_AreTransientAndNeverSingleton()
    {
        var services = Registered();

        foreach (var clientType in ClientTypes)
        {
            var descriptor = Assert.Single(services, candidate => candidate.ServiceType == clientType);
            Assert.Equal(ServiceLifetime.Transient, descriptor.Lifetime);
        }

        var accessor = Assert.Single(services, candidate => candidate.ServiceType == typeof(ICurrentUserAccessor));
        Assert.Equal(ServiceLifetime.Scoped, accessor.Lifetime);
    }

    [Fact]
    public void AddSharpVaultUi_ScopeValidation_Passes()
    {
        var exception = Record.Exception(() => Registered().BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }).Dispose());

        Assert.Null(exception);
    }

    [Fact]
    public void AddSharpVaultUi_ConfigureCallback_SetsTheBaseAddressOnBothClients()
    {
        using var provider = Registered().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();

        foreach (var clientType in ClientTypes)
        {
            Assert.Equal(BaseAddress, factory.CreateClient(clientType.Name).BaseAddress);
        }
    }

    [Fact]
    public async Task AddSharpVaultUi_Clipboard_IsScopedAndResolvesPerCircuit()
    {
        var services = Registered();
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();

        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, candidate => candidate.ServiceType == typeof(SecretClipboard)).Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, Assert.Single(services, candidate => candidate.ServiceType == typeof(IClipboard)).Lifetime);
        Assert.IsType<BrowserClipboard>(first.ServiceProvider.GetRequiredService<IClipboard>());
        Assert.Same(first.ServiceProvider.GetRequiredService<SecretClipboard>(), first.ServiceProvider.GetRequiredService<SecretClipboard>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<SecretClipboard>(), second.ServiceProvider.GetRequiredService<SecretClipboard>());
    }

    [Fact]
    public void AddSharpVaultUi_VaultUiSettings_DefaultsToThirtySecondDelays()
    {
        using var provider = Registered().BuildServiceProvider();

        var settings = provider.GetRequiredService<VaultUiSettings>();

        Assert.Equal(TimeSpan.FromSeconds(30), settings.ClipboardClearDelay);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RevealAutoHideDelay);
    }

    [Fact]
    public void AddSharpVaultUi_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => SharpVaultUiRegistration.AddSharpVaultUi(null!, (_, _) => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSharpVaultUi(null!));
    }

    [Fact]
    public void ApiClients_Sources_RegisterNoDelegatingHandlerThatReadsScopedState()
    {
        var folders = new[]
        {
            Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared"),
            Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Web"),
        };

        var offenders = folders
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("DelegatingHandler", StringComparison.Ordinal) || text.Contains("AddHttpMessageHandler", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }
}
