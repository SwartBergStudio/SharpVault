// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.UnitTests.Support;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption;

public sealed class KeyWrapProviderStartupCheckTests
{
    private const string DevelopmentWarning = "Data keys are wrapped by the development key wrap provider, which is only allowed in the Development environment";

    [Fact]
    public async Task StartAsync_DevelopmentProvider_LogsWarning()
    {
        var logger = new CollectingLogger<KeyWrapProviderStartupCheck>();
        using var provider = new DevelopmentKeyWrapProvider(HostEnvironments.Named("Development"), TestEncryptionOptions.Development());
        var check = new KeyWrapProviderStartupCheck(provider, logger);

        await check.StartAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(DevelopmentWarning, entry.Message);
    }

    [Fact]
    public async Task StartAsync_OtherProvider_LogsInformationWithProviderName()
    {
        var logger = new CollectingLogger<KeyWrapProviderStartupCheck>();
        var check = new KeyWrapProviderStartupCheck(new RecordingKeyWrapProvider(), logger);

        await check.StartAsync(TestContext.Current.CancellationToken);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal("Data keys are wrapped by " + nameof(RecordingKeyWrapProvider), entry.Message);
    }

    [Fact]
    public async Task StartAndStop_Always_MakeNoWrapOrUnwrapCalls()
    {
        var wrapProvider = new RecordingKeyWrapProvider();
        var check = new KeyWrapProviderStartupCheck(wrapProvider, new CollectingLogger<KeyWrapProviderStartupCheck>());

        await check.StartAsync(TestContext.Current.CancellationToken);
        await check.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, wrapProvider.WrapCount);
        Assert.Equal(0, wrapProvider.UnwrapCount);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task StartAsync_Always_LogsNoKeyMaterial()
    {
        var logger = new CollectingLogger<KeyWrapProviderStartupCheck>();
        var material = TestEncryptionOptions.RandomMaterial();
        using var provider = new DevelopmentKeyWrapProvider(HostEnvironments.Named("Development"), TestEncryptionOptions.Development(material));
        var check = new KeyWrapProviderStartupCheck(provider, logger);

        await check.StartAsync(TestContext.Current.CancellationToken);

        Assert.All(logger.Entries, entry =>
        {
            Assert.DoesNotContain(material, entry.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(provider.KeyId, entry.Message, StringComparison.Ordinal);
        });
    }
}
