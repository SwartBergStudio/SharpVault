// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.ServiceDefaults;
using SharpVault.Authentication.Identity;
using SharpVault.ServiceDefaults.Security;
using SharpVault.UI.Shared;
using SharpVault.UI.Shared.Layout;
using SharpVault.UI.Shared.Services.Idle;
using SharpVault.Web;
using SharpVault.Web.Account;
using SharpVault.Web.Api;
using SharpVault.Web.Components;
using SharpVault.Web.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddWebAuthentication();
builder.AddSharpVaultApiClient();
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddHubOptions(options => options.MaximumReceiveMessageSize = WebLimits.MaxHubMessageBytes);
builder.Services.AddSharpVaultRoutedAssemblies(typeof(Program).Assembly);
builder.Services.Configure<IdleLockOptions>(builder.Configuration.GetSection(IdleLockOptions.SectionName));

var app = builder.Build();
var entra = app.Services.GetRequiredService<EntraApplicationSettings>();

app.UseSecurityHeaders(SecurityHeadersPolicy.ForWeb(entra.IdentityProviderOrigin));
app.UseExceptionHandler(WebRoutes.Error, createScopeForErrors: true);
app.UseLocalSignInGuard();
app.UseStatusCodePagesWithReExecute(WebRoutes.NotFound, createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseOutputCache();

app.MapStaticAssets().AllowAnonymous();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
app.MapDefaultEndpoints();

await app.RunAsync();
