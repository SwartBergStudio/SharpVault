// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Authentication.Mode;

namespace SharpVault.UnitTests.Authentication.Mode;

[Trait("Category", "Security")]
public sealed class AuthenticationModeResolverTests
{
    private const string TenantKey = "AzureAd:TenantId";
    private const string ClientKey = "AzureAd:ClientId";
    private const string InstanceKey = "AzureAd:Instance";
    private const string SecretKey = "AzureAd:ClientCredentials:0:ClientSecret";
    private const string TenantValue = "11111111-2222-4333-8444-555555555555";
    private const string ClientValue = "66666666-7777-4888-9999-000000000000";
    private const string InstanceValue = "https://login.microsoftonline.com/";

    private static IConfiguration Configure(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)))
            .Build();

    private static HostingEnvironment Environment(string name) => new() { EnvironmentName = name };

    private static HostApplicationBuilder Builder(string environment, params (string Key, string? Value)[] values)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = environment, DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value)));
        return builder;
    }

    [Fact]
    public void Resolve_BothSettingsSet_ReturnsEntra()
    {
        var mode = AuthenticationModeResolver.Resolve(Configure((TenantKey, TenantValue), (ClientKey, ClientValue)), Environment("Development"));

        Assert.Equal(AuthenticationMode.Entra, mode);
    }

    [Fact]
    public void Resolve_OnlyTenantIdSet_ThrowsNamingClientId()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AuthenticationModeResolver.Resolve(Configure((TenantKey, TenantValue)), Environment("Development")));

        Assert.Contains(ClientKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_OnlyClientIdSet_ThrowsNamingTenantId()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AuthenticationModeResolver.Resolve(Configure((ClientKey, ClientValue)), Environment("Development")));

        Assert.Contains(TenantKey, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_NeitherSetInDevelopment_ReturnsLocal()
    {
        Assert.Equal(AuthenticationMode.Local, AuthenticationModeResolver.Resolve(Configure(), Environment("Development")));
    }

    [Fact]
    public void Resolve_NeitherSetInProduction_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => AuthenticationModeResolver.Resolve(Configure(), Environment("Production")));
    }

    [Fact]
    public void Resolve_WhitespaceValues_AreTreatedAsUnset()
    {
        var mode = AuthenticationModeResolver.Resolve(Configure((TenantKey, " "), (ClientKey, " ")), Environment("Development"));

        Assert.Equal(AuthenticationMode.Local, mode);
    }

    [Fact]
    public void Resolve_InstanceAndSecretOnly_DoesNotSelectEntra()
    {
        var mode = AuthenticationModeResolver.Resolve(Configure((InstanceKey, InstanceValue), (SecretKey, "secret")), Environment("Development"));

        Assert.Equal(AuthenticationMode.Local, mode);
    }

    [Fact]
    public void Resolve_PartialSettings_MessageNamesKeysAndNotValues()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => AuthenticationModeResolver.Resolve(Configure((TenantKey, TenantValue)), Environment("Production")));

        Assert.DoesNotContain(TenantValue, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_MultiTenantValue_StillReachesEntraAndFailsStrictRead()
    {
        var builder = Builder("Development", (TenantKey, "common"), (ClientKey, ClientValue), (InstanceKey, InstanceValue));

        Assert.Throws<InvalidOperationException>(() => builder.AddAuthenticationMode());
    }

    [Fact]
    public void AddAuthenticationMode_Local_RegistersLocalEntraSettings()
    {
        var builder = Builder("Development");

        var info = builder.AddAuthenticationMode();

        Assert.Equal(AuthenticationMode.Local, info.Mode);
        Assert.Same(LocalAuthenticationDefaults.EntraSettings, info.Entra);
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Same(info, provider.GetRequiredService<AuthenticationModeInfo>());
        Assert.Same(info.Entra, provider.GetRequiredService<EntraApplicationSettings>());
    }

    [Fact]
    public void AddAuthenticationMode_Entra_RegistersConfiguredSettings()
    {
        var builder = Builder("Production", (TenantKey, TenantValue), (ClientKey, ClientValue), (InstanceKey, InstanceValue));

        var info = builder.AddAuthenticationMode();

        Assert.Equal(AuthenticationMode.Entra, info.Mode);
        Assert.Equal(Guid.Parse(TenantValue), info.Entra.TenantId);
        Assert.Equal(Guid.Parse(ClientValue), info.Entra.ClientId);
        using var provider = builder.Services.BuildServiceProvider();
        Assert.Same(info.Entra, provider.GetRequiredService<EntraApplicationSettings>());
    }
}
