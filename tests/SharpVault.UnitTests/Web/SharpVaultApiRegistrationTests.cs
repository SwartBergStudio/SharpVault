// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.Mode;
using SharpVault.UI.Shared.Services.Auth;
using WebApp::SharpVault.Web.Api;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

public sealed class SharpVaultApiRegistrationTests : IDisposable
{
    private const string TenantId = "5f0c1d2e-4b7a-4c3d-9e8f-00000000a001";
    private const string ClientId = "5f0c1d2e-4b7a-4c3d-9e8f-00000000a002";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sharpvault-unit-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void AddSharpVaultApiClient_LocalMode_UsesTheLocalProviderAndNeedsNoScopes()
    {
        var builder = CreateBuilder(entra: false);

        builder.AddSharpVaultApiClient();

        var provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IApiAccessTokenProvider));
        Assert.Equal(typeof(LocalApiAccessTokenProvider), provider.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, provider.Lifetime);
        Assert.True(Validate(builder, new SharpVaultApiOptions { BaseAddress = new Uri("https://api.example.test/") }).Succeeded);
    }

    [Fact]
    public void AddSharpVaultApiClient_EntraMode_UsesTheEntraProviderAndRequiresScopes()
    {
        var builder = CreateBuilder(entra: true);

        builder.AddSharpVaultApiClient();

        var provider = Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(IApiAccessTokenProvider));
        Assert.Equal(typeof(EntraApiAccessTokenProvider), provider.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, provider.Lifetime);
        Assert.True(Validate(builder, new SharpVaultApiOptions { BaseAddress = new Uri("https://api.example.test/") }).Failed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AddSharpVaultApiClient_DefaultHttpClient_HasNoProofHandler()
    {
        var builder = CreateBuilder(entra: false);
        builder.AddSharpVaultApiClient();
        using var provider = builder.Services.BuildServiceProvider();

        var chain = HandlerChainTypes(provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(string.Empty));

        Assert.DoesNotContain(typeof(WebGatewayProofHandler), chain);
    }

    [Fact]
    public void AddWebAuthentication_LocalCookieOptions_EqualEntraCookieOptions()
    {
        var local = CreateBuilder(entra: false);
        local.AddWebAuthentication();
        var entra = CreateBuilder(entra: true);
        entra.AddWebAuthentication();

        var localOptions = CookieOptions(local);
        var entraOptions = CookieOptions(entra);

        Assert.Equal(entraOptions.Cookie.Name, localOptions.Cookie.Name);
        Assert.Equal("__Host-SharpVault", localOptions.Cookie.Name);
        Assert.Equal(entraOptions.Cookie.SecurePolicy, localOptions.Cookie.SecurePolicy);
        Assert.Equal(CookieSecurePolicy.Always, localOptions.Cookie.SecurePolicy);
        Assert.Equal(entraOptions.Cookie.HttpOnly, localOptions.Cookie.HttpOnly);
        Assert.True(localOptions.Cookie.HttpOnly);
        Assert.Equal(entraOptions.Cookie.SameSite, localOptions.Cookie.SameSite);
        Assert.Equal(entraOptions.Cookie.Path, localOptions.Cookie.Path);
        Assert.Equal(entraOptions.ExpireTimeSpan, localOptions.ExpireTimeSpan);
        Assert.Equal(TimeSpan.FromHours(8), localOptions.ExpireTimeSpan);
        Assert.Equal(entraOptions.SlidingExpiration, localOptions.SlidingExpiration);
        Assert.Equal(entraOptions.AccessDeniedPath, localOptions.AccessDeniedPath);
    }

    [Fact]
    public void AddWebAuthentication_EntraMode_NeverCreatesTheKeyFile()
    {
        var builder = CreateBuilder(entra: true);

        builder.AddWebAuthentication();

        Assert.False(Directory.Exists(_directory));
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(LocalSigningKey));
    }

    private static List<Type> HandlerChainTypes(HttpMessageHandler handler)
    {
        var types = new List<Type>();
        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            types.Add(current.GetType());
        }

        return types;
    }

    private static CookieAuthenticationOptions CookieOptions(HostApplicationBuilder builder)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private static ValidateOptionsResult Validate(HostApplicationBuilder builder, SharpVaultApiOptions options)
    {
        using var provider = builder.Services.BuildServiceProvider();
        return provider.GetRequiredService<IValidateOptions<SharpVaultApiOptions>>().Validate(Options.DefaultName, options);
    }

    private HostApplicationBuilder CreateBuilder(bool entra)
    {
        var settings = new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            Configuration = new ConfigurationManager(),
            DisableDefaults = true,
        };
        var values = new Dictionary<string, string?>
        {
            ["LocalAuth:KeyFilePath"] = Path.Combine(_directory, "dev-auth-key"),
            ["SharpVaultApi:BaseAddress"] = "https://api.example.test/",
        };
        if (entra)
        {
            values["AzureAd:TenantId"] = TenantId;
            values["AzureAd:ClientId"] = ClientId;
            values["AzureAd:Instance"] = "https://login.microsoftonline.com/";
        }

        settings.Configuration.AddInMemoryCollection(values);
        var builder = Host.CreateApplicationBuilder(settings);
        var resolved = AuthenticationModeResolver.Resolve(builder.Configuration, builder.Environment);
        Assert.Equal(entra ? AuthenticationMode.Entra : AuthenticationMode.Local, resolved);
        return builder;
    }
}
