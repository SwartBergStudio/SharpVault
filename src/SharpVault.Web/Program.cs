// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.ServiceDefaults;
using SharpVault.ServiceDefaults.Security;
using SharpVault.Web;
using SharpVault.Web.Components;
using SharpVault.UI.Shared.Layout;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

app.UseSecurityHeaders(SecurityHeadersPolicy.ForWeb(new Uri("https://login.microsoftonline.com/")));
app.UseExceptionHandler(WebRoutes.Error, createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute(WebRoutes.NotFound, createScopeForStatusCodePages: true);
app.UseAntiforgery();
app.UseOutputCache();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(MainLayout).Assembly);
app.MapDefaultEndpoints();

await app.RunAsync();
