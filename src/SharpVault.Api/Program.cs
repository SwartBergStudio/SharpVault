// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Api.Endpoints;
using SharpVault.Api.Endpoints.Sharing;
using SharpVault.Api.Endpoints.Transfers;
using SharpVault.Api.Endpoints.Vault;
using SharpVault.Api.Errors;
using SharpVault.Api.OpenApi;
using SharpVault.Api.RateLimiting;
using SharpVault.Api.Security;
using SharpVault.Application;
using SharpVault.Infrastructure;
using SharpVault.ServiceDefaults;
using SharpVault.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddApplication();
builder.AddInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddExceptionHandler<ValidationExceptionHandler>();
builder.Services.AddExceptionHandler<SecretDecryptionExceptionHandler>();
builder.AddApiAuthentication();
builder.AddUserDirectory();
builder.Services.AddApiDocumentation();
builder.AddVaultRateLimiting();

var app = builder.Build();

app.UseSecurityHeaders(SecurityHeadersPolicy.ForApi(app.Environment.IsDevelopment()));
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseOutputCache();

app.MapApiDocumentation();
app.MapMeEndpoints();
app.MapAdminUserEndpoints();
app.MapVaultSecretEndpoints();
app.MapVaultHistoryEndpoints();
app.MapVaultFolderEndpoints();
app.MapRecycleBinEndpoints();
app.MapSharedItemEndpoints();
app.MapShareGrantEndpoints();
app.MapDirectoryEndpoints();
app.MapAdminTransferEndpoints();
app.MapTransferNoticeEndpoints();
app.MapDefaultEndpoints();

await app.RunAsync();
