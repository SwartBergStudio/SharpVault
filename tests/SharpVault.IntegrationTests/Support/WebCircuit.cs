// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharpVault.IntegrationTests.Authentication;
using SharpVault.IntegrationTests.Factories;
using SharpVault.UI.Shared.Services.Auth;
using SharpVault.UI.Shared.Services.Clients;
using SharpVault.UI.Shared.Services.Users;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.IntegrationTests.Support;

public sealed class WebCircuit : IDisposable
{
    private readonly IServiceScope _scope;

    private WebCircuit(IServiceScope scope)
    {
        _scope = scope;
    }

    public IMeApiClient Me => _scope.ServiceProvider.GetRequiredService<IMeApiClient>();

    public IAdminUsersApiClient AdminUsers => _scope.ServiceProvider.GetRequiredService<IAdminUsersApiClient>();

    public IVaultSecretsApiClient VaultSecrets => _scope.ServiceProvider.GetRequiredService<IVaultSecretsApiClient>();

    public ISecretHistoryApiClient SecretHistory => _scope.ServiceProvider.GetRequiredService<ISecretHistoryApiClient>();

    public IVaultFoldersApiClient VaultFolders => _scope.ServiceProvider.GetRequiredService<IVaultFoldersApiClient>();

    public IRecycleBinApiClient RecycleBin => _scope.ServiceProvider.GetRequiredService<IRecycleBinApiClient>();

    public ISharedItemsApiClient SharedItems => _scope.ServiceProvider.GetRequiredService<ISharedItemsApiClient>();

    public IShareGrantsApiClient ShareGrants => _scope.ServiceProvider.GetRequiredService<IShareGrantsApiClient>();

    public IDirectoryApiClient Directory => _scope.ServiceProvider.GetRequiredService<IDirectoryApiClient>();

    public ICurrentUserAccessor CurrentUser => _scope.ServiceProvider.GetRequiredService<ICurrentUserAccessor>();

    public IAdminTransfersApiClient AdminTransfers => _scope.ServiceProvider.GetRequiredService<IAdminTransfersApiClient>();

    public ITransferNoticesApiClient TransferNotices => _scope.ServiceProvider.GetRequiredService<ITransferNoticesApiClient>();

    public static WebCircuit For(WebTestFactory web, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(user);

        return For(web.Services, TestCookiePrincipal.Create(user));
    }

    public static WebCircuit For(WebTestFactory web, TestUser user, string browserAddress)
    {
        ArgumentNullException.ThrowIfNull(web);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(browserAddress);

        var principal = TestCookiePrincipal.Create(user);
        ClientAddressClaim.AddTo(principal, IPAddress.Parse(browserAddress));
        return For(web.Services, principal);
    }

    public static WebCircuit For(IServiceProvider services, ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(principal);

        if (principal.Identity is ClaimsIdentity identity && identity.FindFirst(SessionClaims.SessionId) is null)
        {
            SessionClaimsIssuer.Add(identity, services.GetRequiredService<TimeProvider>());
        }

        var scope = services.CreateScope();
        var provider = (IHostEnvironmentAuthenticationStateProvider)scope.ServiceProvider.GetRequiredService<AuthenticationStateProvider>();
        provider.SetAuthenticationState(Task.FromResult(new AuthenticationState(principal)));
        return new WebCircuit(scope);
    }

    public void Dispose() => _scope.Dispose();
}
