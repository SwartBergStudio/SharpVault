// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;
using SharpVault.UI.Shared.Navigation;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Clipboard;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.UI.Shared.Services.Users;

namespace SharpVault.UI.Shared;

public static class SharpVaultUiRegistration
{
    private const int SnackbarVisibleMilliseconds = 4000;

    public static IServiceCollection AddSharpVaultUi(
        this IServiceCollection services,
        Action<IServiceProvider, HttpClient> configureApiClient,
        Action<IHttpClientBuilder>? configureApiClientBuilder = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureApiClient);

        services.AddMudServices(options =>
        {
            options.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomRight;
            options.SnackbarConfiguration.VisibleStateDuration = SnackbarVisibleMilliseconds;
            options.SnackbarConfiguration.PreventDuplicates = true;
            options.SnackbarConfiguration.ClearAfterNavigation = true;
        });
        AddApiClient<IMeApiClient, MeApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IAdminUsersApiClient, AdminUsersApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IVaultSecretsApiClient, VaultSecretsApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<ISecretHistoryApiClient, SecretHistoryApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IVaultFoldersApiClient, VaultFoldersApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IRecycleBinApiClient, RecycleBinApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<ISharedItemsApiClient, SharedItemsApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IShareGrantsApiClient, ShareGrantsApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IDirectoryApiClient, DirectoryApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<IAdminTransfersApiClient, AdminTransfersApiClient>(services, configureApiClient, configureApiClientBuilder);
        AddApiClient<ITransferNoticesApiClient, TransferNoticesApiClient>(services, configureApiClient, configureApiClientBuilder);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(VaultUiSettings.Default);
        services.TryAddScoped<IClipboard, BrowserClipboard>();
        services.AddScoped<SecretClipboard>();
        services.AddScoped<ICurrentUserAccessor, CurrentUserAccessor>();
        services.AddScoped<IIdleMonitor, IdleMonitor>();
        services.AddOptions<IdleLockOptions>().ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<IdleLockOptions>, IdleLockOptionsValidator>());
        services.TryAddSingleton(new RoutedAssemblies([]));
        return services;
    }

    public static IServiceCollection AddSharpVaultRoutedAssemblies(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assemblies);

        services.RemoveAll<RoutedAssemblies>();
        services.AddSingleton(new RoutedAssemblies(assemblies));
        return services;
    }

    private static void AddApiClient<TClient, TImplementation>(
        IServiceCollection services,
        Action<IServiceProvider, HttpClient> configureApiClient,
        Action<IHttpClientBuilder>? configureApiClientBuilder)
        where TClient : class
        where TImplementation : class, TClient
    {
        var builder = services.AddHttpClient<TClient, TImplementation>(configureApiClient);
        configureApiClientBuilder?.Invoke(builder);
    }
}
