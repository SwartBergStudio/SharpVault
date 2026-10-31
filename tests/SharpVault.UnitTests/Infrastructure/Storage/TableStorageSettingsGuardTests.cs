// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Aspire.Azure.Data.Tables;
using AutoFixture;
using SharpVault.Infrastructure.Storage;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Storage;

[Trait("Category", "Security")]
public sealed class TableStorageSettingsGuardTests
{
    private const string AzuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;TableEndpoint=http://127.0.0.1:10002/devstoreaccount1;";
    private const string AzuriteAccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string ValidServiceUri = "https://sharpvaultprod.table.core.windows.net/";

    private readonly Fixture _fixture = new();

    [Fact]
    public void EnsureAllowed_DevelopmentWithAzuriteConnectionString_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Development")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_DevelopmentWithHttpsServiceUri_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(ValidServiceUri) };

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

    [Fact]
    public void EnsureAllowed_StagingWithHttpsServiceUri_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(ValidServiceUri) };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Staging")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithHttpsServiceUriAndWhitespaceConnectionString_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = "   ", ServiceUri = new Uri(ValidServiceUri) };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithHttpsServiceUriWithPortAndPath_DoesNotThrow()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("https://sharpvaultprod.table.core.windows.net:443/path") };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithAccountKeyConnectionString_ThrowsWithoutEchoingValue()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
        Assert.DoesNotContain("AccountKey=", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(AzuriteAccountKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("devstoreaccount1", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithRandomAccountKey_ThrowsWithoutEchoingValue()
    {
        var accountKey = Convert.ToBase64String(_fixture.CreateMany<byte>(64).ToArray());
        var accountName = "acct" + _fixture.Create<Guid>().ToString("N")[..8];
        var settings = new AzureDataTablesSettings
        {
            ConnectionString = $"DefaultEndpointsProtocol=https;AccountName={accountName};AccountKey={accountKey};EndpointSuffix=core.windows.net",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.DoesNotContain(accountKey, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(accountName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithUseDevelopmentStorage_Throws()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = "UseDevelopmentStorage=true" };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithSharedAccessSignatureConnectionString_ThrowsWithoutEchoingValue()
    {
        var signature = _fixture.Create<Guid>().ToString("N");
        var settings = new AzureDataTablesSettings
        {
            ConnectionString = $"TableEndpoint=https://sharpvaultprod.table.core.windows.net/;SharedAccessSignature=sv=2024-01-01&sig={signature}",
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
        Assert.DoesNotContain(signature, exception.Message, StringComparison.Ordinal);
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
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Dev")]
    [InlineData("Development2")]
    [InlineData("")]
    public void EnsureAllowed_AnyNonDevelopmentEnvironmentWithConnectionString_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.ConnectionStringNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Dev")]
    [InlineData("")]
    public void EnsureAllowed_AnyNonDevelopmentEnvironmentWithHttpServiceUri_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("http://sharpvaultprod.table.core.windows.net/") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithSasQueryOnServiceUri_Throws()
    {
        var signature = _fixture.Create<Guid>().ToString("N");
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri($"https://sharpvaultprod.table.core.windows.net/?sv=2024-01-01&sig={signature}") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
        Assert.DoesNotContain(signature, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("sig=", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithQueryOnPathedServiceUri_Throws()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("https://sharpvaultprod.table.core.windows.net/account?x=1") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_ProductionWithHttpServiceUri_Throws()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri("http://127.0.0.1:10002/devstoreaccount1") };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
        Assert.DoesNotContain("127.0.0.1", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ftp://sharpvaultprod.table.core.windows.net/")]
    [InlineData("file:///var/data/tables")]
    [InlineData("ws://sharpvaultprod.table.core.windows.net/")]
    [InlineData("wss://sharpvaultprod.table.core.windows.net/")]
    public void EnsureAllowed_ProductionWithNonHttpsScheme_Throws(string serviceUri)
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(serviceUri) };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Production")));

        Assert.Equal(TableStorageMessageText.ServiceUriNotAllowed, exception.Message);
    }

    [Theory]
    [InlineData("https://owner:s3cretPassw0rd@sharpvaultprod.table.core.windows.net/", "s3cretPassw0rd")]
    [InlineData("https://s3cretUser@sharpvaultprod.table.core.windows.net/", "s3cretUser")]
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
    [InlineData("Development")]
    [InlineData("Production")]
    public void EnsureAllowed_NothingConfigured_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings();

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.NotConfigured, exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void EnsureAllowed_WhitespaceConnectionStringAndNoUri_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = " \t " };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.NotConfigured, exception.Message);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void EnsureAllowed_EmptyConnectionStringAndNoUri_Throws(string environmentName)
    {
        var settings = new AzureDataTablesSettings { ConnectionString = string.Empty };

        var exception = Assert.Throws<InvalidOperationException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named(environmentName)));

        Assert.Equal(TableStorageMessageText.NotConfigured, exception.Message);
    }

    [Fact]
    public void EnsureAllowed_NullSettings_ThrowsArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => TableStorageSettingsGuard.EnsureAllowed(null!, HostEnvironments.Named("Production")));

        Assert.Equal("settings", exception.ParamName);
    }

    [Fact]
    public void EnsureAllowed_NullEnvironment_ThrowsArgumentNullException()
    {
        var settings = new AzureDataTablesSettings { ServiceUri = new Uri(ValidServiceUri) };

        var exception = Assert.Throws<ArgumentNullException>(
            () => TableStorageSettingsGuard.EnsureAllowed(settings, null!));

        Assert.Equal("environment", exception.ParamName);
    }

    [Fact]
    public void EnsureAllowed_DevelopmentWithNonDevelopmentLookalikeName_DoesNotTreatItAsDevelopment()
    {
        var settings = new AzureDataTablesSettings { ConnectionString = AzuriteConnectionString };

        var exception = Record.Exception(() => TableStorageSettingsGuard.EnsureAllowed(settings, HostEnvironments.Named("Development ")));

        Assert.IsType<InvalidOperationException>(exception);
    }
}
