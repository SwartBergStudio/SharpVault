// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Aspire.Azure.Data.Tables;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

[Trait("Category", "Security")]
public sealed class TableStorageSettingsGuardTests
{
    private const string AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
    private const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string SasSignature = "0123456789abcdef0123456789abcdef";
    private const string ValidServiceUri = "https://sharpvaultprod.table.core.windows.net/";

    [Fact]
    public void EnsureAllowed_DevelopmentWithAzuriteConnectionString_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Development")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_DevelopmentWithHttpServiceUri_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("http://127.0.0.1:10002/devstoreaccount1") };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Development")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithHttpsServiceUri_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(ValidServiceUri) };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(AzuriteConnectionString, AzuriteAccountKey)]
    [InlineData("TableEndpoint=https://sharpvaultprod.table.core.windows.net/;SharedAccessSignature=sv=2024-01-01&sig=" + SasSignature, SasSignature)]
    public void EnsureAllowed_ProductionWithSecretInConnectionString_ThrowsWithoutEchoingValue(string connectionString, string secret)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = connectionString };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithConnectionStringAndServiceUri_Throws()
    {
        var settings = new AzureDataTablesSettings
        {
            ConnectionString = AzuriteConnectionString,
            ServiceUri = new Uri(ValidServiceUri),
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")]
    public void EnsureAllowed_NonDevelopmentEnvironmentWithConnectionString_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("")]
    public void EnsureAllowed_NonDevelopmentEnvironmentWithHttpServiceUri_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("http://sharpvaultprod.table.core.windows.net/") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithSasQueryOnServiceUri_ThrowsWithoutEchoingSignature()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri($"https://sharpvaultprod.table.core.windows.net/?sv=2024-01-01&sig={SasSignature}") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
        Assert.DoesNotContain(SasSignature, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://sharpvaultprod.table.core.windows.net/")]
    [InlineData("file:///var/data/tables")]
    public void EnsureAllowed_ProductionWithNonHttpsScheme_Throws(string serviceUri)
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(serviceUri) };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData("https://owner:s3cretPassw0rd@sharpvaultprod.table.core.windows.net/", "s3cretPassw0rd")]
    [InlineData("https://:s3cretPassw0rd@sharpvaultprod.table.core.windows.net/", "s3cretPassw0rd")]
    public void EnsureAllowed_ProductionWithUserInfoInServiceUri_ThrowsWithoutEchoingValue(string serviceUri, string secret)
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(serviceUri) };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
        Assert.DoesNotContain(secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithRelativeServiceUri_Throws()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("/devstoreaccount1", UriKind.Relative) };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t ")]
    public void EnsureAllowed_NoConnectionStringAndNoUri_Throws(string? connectionString)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = connectionString };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.NotConfigured, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_DevelopmentWithNonDevelopmentLookalikeName_DoesNotTreatItAsDevelopment()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Development ")));

        Assert.IsType<InvalidOperationException>(exception);
    }
}
