// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.Extensions.DependencyInjection;
using SharpVault.UI.Shared.Services.Clients;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.IntegrationTests.Support;

internal static class ApiClientHandlers
{
    public static IServiceCollection UsePrimaryHandler(this IServiceCollection services, Func<HttpMessageHandler> handlerFactory)
    {
        services.AddHttpClient<IMeApiClient, MeApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IAdminUsersApiClient, AdminUsersApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IVaultSecretsApiClient, VaultSecretsApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<ISecretHistoryApiClient, SecretHistoryApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IVaultFoldersApiClient, VaultFoldersApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IRecycleBinApiClient, RecycleBinApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<ISharedItemsApiClient, SharedItemsApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IShareGrantsApiClient, ShareGrantsApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IDirectoryApiClient, DirectoryApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<IAdminTransfersApiClient, AdminTransfersApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        services.AddHttpClient<ITransferNoticesApiClient, TransferNoticesApiClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        return services.UseSharedSessionHandler(handlerFactory);
    }

    public static IServiceCollection UseSharedSessionHandler(this IServiceCollection services, Func<HttpMessageHandler> handlerFactory)
    {
        services.AddHttpClient<ISharedSessionClient, ApiSharedSessionClient>()
            .ConfigurePrimaryHttpMessageHandler(handlerFactory);
        return services;
    }
}
