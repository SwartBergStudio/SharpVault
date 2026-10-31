// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

extern alias WebApp;

using Microsoft.Extensions.Configuration;
using SharpVault.UnitTests.Support;
using WebApp::SharpVault.Web.Security;

namespace SharpVault.UnitTests.Web;

/// <summary>
/// W10: outside Development the web app authenticates with a federated or certificate credential, never a client secret.
/// </summary>
[Trait("Category", "Security")]
public sealed class WebCredentialGuardTests
{
    private const string Secret = "s3cr3t-value-that-must-not-leak";
    private const string SecretMessage = "Outside the Development environment the web app must authenticate to Entra ID with a managed identity federated credential or a certificate. Client secrets are not allowed.";
    private const string CredentialMessage = "Outside the Development environment AzureAd:ClientCredentials must contain at least one managed identity or certificate credential.";

    private static IConfiguration Section(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> ManagedIdentity() =>
        new() { ["ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity" };

    [Fact]
    public void EnsureAllowed_DevelopmentWithClientSecret_DoesNotThrow()
    {
        var section = Section(new() { ["ClientSecret"] = Secret });

        var exception = Record.Exception(() => WebCredentialGuard.EnsureAllowed(section, HostEnvironments.Named("Development")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_DevelopmentWithoutAnyCredential_DoesNotThrow()
    {
        var exception = Record.Exception(() => WebCredentialGuard.EnsureAllowed(Section([]), HostEnvironments.Named("Development")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("SignedAssertionFromManagedIdentity")]
    [InlineData("KeyVault")]
    [InlineData("StoreWithThumbprint")]
    [InlineData("StoreWithDistinguishedName")]
    [InlineData("signedassertionfrommanagedidentity")]
    public void EnsureAllowed_ProductionWithManagedIdentityCredential_DoesNotThrow(string sourceType)
    {
        var section = Section(new() { ["ClientCredentials:0:SourceType"] = sourceType });

        var exception = Record.Exception(() => WebCredentialGuard.EnsureAllowed(section, HostEnvironments.Named("Production")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("")]
    public void EnsureAllowed_AnyNonDevelopmentEnvironmentWithClientSecret_Throws(string environment)
    {
        var section = Section(new() { ["ClientSecret"] = Secret });

        Assert.Throws<InvalidOperationException>(() => WebCredentialGuard.EnsureAllowed(section, HostEnvironments.Named(environment)));
    }

    public static TheoryData<string, Dictionary<string, string?>> SecretConfigurations() =>
        new()
        {
            { "legacyKey", new() { ["ClientSecret"] = Secret, ["ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity" } },
            { "sourceType", new() { ["ClientCredentials:0:SourceType"] = "ClientSecret", ["ClientCredentials:0:ClientSecret"] = Secret } },
            { "casing", new() { ["ClientCredentials:0:SourceType"] = "clientsecret", ["ClientCredentials:0:ClientSecret"] = Secret } },
            { "upperCasing", new() { ["ClientCredentials:0:SourceType"] = "CLIENTSECRET", ["ClientCredentials:0:ClientSecret"] = Secret } },
            { "valueOnManagedIdentityEntry", new() { ["ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity", ["ClientCredentials:0:ClientSecret"] = Secret } },
            { "secondEntry", new() { ["ClientCredentials:0:SourceType"] = "SignedAssertionFromManagedIdentity", ["ClientCredentials:1:SourceType"] = "ClientSecret", ["ClientCredentials:1:ClientSecret"] = Secret } },
            { "sourceTypeWithoutValue", new() { ["ClientCredentials:0:SourceType"] = "ClientSecret" } },
            { "lowerCaseKey", new() { ["clientsecret"] = Secret, ["ClientCredentials:0:SourceType"] = "KeyVault" } },
        };

    [Theory]
    [MemberData(nameof(SecretConfigurations))]
    public void EnsureAllowed_ProductionWithClientSecret_ThrowsWithoutEchoingSecret(string caseName, Dictionary<string, string?> values)
    {
        Assert.NotEmpty(caseName);

        var exception = Assert.Throws<InvalidOperationException>(
            () => WebCredentialGuard.EnsureAllowed(Section(values), HostEnvironments.Named("Production")));

        Assert.Equal(SecretMessage, exception.Message);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithoutCredential_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => WebCredentialGuard.EnsureAllowed(Section([]), HostEnvironments.Named("Production")));

        Assert.Equal(CredentialMessage, exception.Message);
    }

    [Theory]
    [InlineData("None")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void EnsureAllowed_ProductionWithOnlyUnrecognisedCredential_Throws(string sourceType)
    {
        var section = Section(new() { ["ClientCredentials:0:SourceType"] = sourceType });

        var exception = Assert.Throws<InvalidOperationException>(
            () => WebCredentialGuard.EnsureAllowed(section, HostEnvironments.Named("Production")));

        Assert.Equal(CredentialMessage, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithBlankLegacySecretAndManagedIdentity_DoesNotThrow()
    {
        var values = ManagedIdentity();
        values["ClientSecret"] = string.Empty;

        var exception = Record.Exception(() => WebCredentialGuard.EnsureAllowed(Section(values), HostEnvironments.Named("Production")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => WebCredentialGuard.EnsureAllowed(null!, HostEnvironments.Named("Production")));
        Assert.Throws<ArgumentNullException>(() => WebCredentialGuard.EnsureAllowed(Section([]), null!));
    }
}
