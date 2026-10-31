// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Configuration;
using SharpVault.ServiceDefaults.Identity;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.ServiceDefaults.Identity;

/// <summary>
/// TEN-1 and TEN-2: only a single named tenant over https is ever accepted.
/// </summary>
[Trait("Category", "Security")]
public sealed class EntraApplicationSettingsTests
{
    private const string TenantMessage = "AzureAd:TenantId must be the directory (tenant) id GUID. Multi-tenant values such as common, organizations and consumers are not allowed.";
    private const string ClientMessage = "AzureAd:ClientId must be the application (client) id GUID.";
    private const string InstanceMessage = "AzureAd:Instance must be an absolute https URI with no path, query or user information.";

    private static readonly string ValidTenant = TestIdentities.TenantId.ToString("D");
    private static readonly string ValidClient = TestIdentities.ClientId.ToString("D");

    private static IConfiguration Section(string? instance, string? tenant, string? client)
    {
        var values = new Dictionary<string, string?> { ["Instance"] = instance, ["TenantId"] = tenant, ["ClientId"] = client };
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Read_ValidSettings_ReturnsTenantClientAndIssuers()
    {
        var settings = EntraApplicationSettings.Read(Section("https://login.microsoftonline.com/", ValidTenant, ValidClient));

        Assert.Equal(TestIdentities.TenantId, settings.TenantId);
        Assert.Equal(TestIdentities.ClientId, settings.ClientId);
        Assert.Equal($"https://login.microsoftonline.com/{ValidTenant}/v2.0", settings.V2Issuer);
        Assert.Equal($"https://sts.windows.net/{ValidTenant}/", settings.V1Issuer);
        Assert.Equal(new Uri("https://login.microsoftonline.com"), settings.IdentityProviderOrigin);
    }

    [Fact]
    public void Read_InstanceWithoutTrailingSlash_IsAccepted()
    {
        var settings = EntraApplicationSettings.Read(Section("https://login.microsoftonline.com", ValidTenant, ValidClient));

        Assert.Equal($"https://login.microsoftonline.com/{ValidTenant}/v2.0", settings.V2Issuer);
    }

    [Fact]
    public void Read_UppercaseGuids_AreAccepted()
    {
        var settings = EntraApplicationSettings.Read(Section("https://login.microsoftonline.com/", ValidTenant.ToUpperInvariant(), ValidClient.ToUpperInvariant()));

        Assert.Equal(TestIdentities.TenantId, settings.TenantId);
        Assert.Equal(ValidTenant, settings.TenantId.ToString("D"));
    }

    [Theory]
    [InlineData("TenantId", "common")]
    [InlineData("TenantId", "organizations")]
    [InlineData("TenantId", "consumers")]
    [InlineData("TenantId", "contoso.onmicrosoft.com")]
    [InlineData("TenantId", "")]
    [InlineData("TenantId", " ")]
    [InlineData("TenantId", null)]
    [InlineData("TenantId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("TenantId", "{3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b}")]
    [InlineData("TenantId", "3f2a1c9e7b4d4e8a9c215d6e7f809a1b")]
    [InlineData("TenantId", "(3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b)")]
    [InlineData("TenantId", "3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b/../common")]
    [InlineData("ClientId", "abc")]
    [InlineData("ClientId", "")]
    [InlineData("ClientId", null)]
    [InlineData("ClientId", "00000000-0000-0000-0000-000000000000")]
    [InlineData("ClientId", "{a1b2c3d4-e5f6-4708-8192-a3b4c5d6e7f8}")]
    [InlineData("Instance", "http://login.microsoftonline.com/")]
    [InlineData("Instance", "https://login.microsoftonline.com/tenant/")]
    [InlineData("Instance", "https://user:pw@login.microsoftonline.com/")]
    [InlineData("Instance", "https://login.microsoftonline.com/?x=1")]
    [InlineData("Instance", "https://login.microsoftonline.com/#fragment")]
    [InlineData("Instance", "/relative/path")]
    [InlineData("Instance", "login.microsoftonline.com")]
    [InlineData("Instance", "")]
    [InlineData("Instance", null)]
    [InlineData("Instance", "ftp://login.microsoftonline.com/")]
    public void Read_InvalidSettings_ThrowsConstantMessage(string field, string? value)
    {
        var instance = field == "Instance" ? value : "https://login.microsoftonline.com/";
        var tenant = field == "TenantId" ? value : ValidTenant;
        var client = field == "ClientId" ? value : ValidClient;
        var expected = field switch
        {
            "TenantId" => TenantMessage,
            "ClientId" => ClientMessage,
            _ => InstanceMessage,
        };

        var exception = Assert.Throws<InvalidOperationException>(() => EntraApplicationSettings.Read(Section(instance, tenant, client)));

        Assert.Equal(expected, exception.Message);
    }

    [Theory]
    [InlineData("TenantId", "contoso.onmicrosoft.com")]
    [InlineData("TenantId", "{3f2a1c9e-7b4d-4e8a-9c21-5d6e7f809a1b}")]
    [InlineData("TenantId", "3f2a1c9e7b4d4e8a9c215d6e7f809a1b")]
    [InlineData("ClientId", "abc-secret-looking-value")]
    [InlineData("Instance", "https://user:pw@login.microsoftonline.com/")]
    [InlineData("Instance", "https://login.microsoftonline.com/?x=1")]
    [InlineData("Instance", "http://evil.example.test/")]
    public void Read_InvalidSettings_ThrowsWithoutEchoingValue(string field, string value)
    {
        var instance = field == "Instance" ? value : "https://login.microsoftonline.com/";
        var tenant = field == "TenantId" ? value : ValidTenant;
        var client = field == "ClientId" ? value : ValidClient;

        var exception = Assert.Throws<InvalidOperationException>(() => EntraApplicationSettings.Read(Section(instance, tenant, client)));

        Assert.DoesNotContain(value, exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pw", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("evil.example", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ValidTenant, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_EverythingMissing_ThrowsInstanceMessageFirst()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => EntraApplicationSettings.Read(Section(null, null, null)));

        Assert.Equal(InstanceMessage, exception.Message);
    }

    [Fact]
    public void Read_NullSection_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => EntraApplicationSettings.Read(null!));
    }

    [Theory]
    [InlineData("https://login.microsoftonline.com/", "https://login.microsoftonline.com")]
    [InlineData("https://login.microsoftonline.us/", "https://login.microsoftonline.us")]
    public void V2Issuer_Instance_UsesTenantSpecificV2Path(string instance, string authority)
    {
        var settings = EntraApplicationSettings.Read(Section(instance, ValidTenant, ValidClient));

        Assert.Equal($"{authority}/{ValidTenant}/v2.0", settings.V2Issuer);
        Assert.DoesNotContain("common", settings.V2Issuer, StringComparison.Ordinal);
        Assert.DoesNotContain("organizations", settings.V2Issuer, StringComparison.Ordinal);
    }

    [Fact]
    public void V2Issuer_Tenant_IsLowercaseCanonicalGuid()
    {
        var settings = EntraApplicationSettings.Read(Section("https://login.microsoftonline.com/", ValidTenant.ToUpperInvariant(), ValidClient));

        Assert.Equal(settings.V2Issuer, settings.V2Issuer.ToLowerInvariant());
    }
}
