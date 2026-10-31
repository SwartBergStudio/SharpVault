// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Api.OpenApi;
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
builder.Services.AddApiAuthentication();
builder.Services.AddApiDocumentation();

var app = builder.Build();

app.UseSecurityHeaders(SecurityHeadersPolicy.ForApi(app.Environment.IsDevelopment()));
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseOutputCache();

app.MapApiDocumentation();
app.MapDefaultEndpoints();

await app.RunAsync();
