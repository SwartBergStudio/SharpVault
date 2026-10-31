// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.UnitTests.Support.Encryption;

namespace SharpVault.UnitTests.Infrastructure.Encryption.KeyVault;

public sealed class KeyVaultCryptographyClientFactoryTests
{
    private const string KeyIdPrefix = FakeKeyVault.VaultUri + "/keys/" + FakeKeyVault.KeyName + "/";
    private const int CacheLimit = KeyVaultCryptographyClientFactory.MaxCachedClients;
    private const int ExpectedRetries = 3;
    private const int ExpectedTimeoutSeconds = 10;

    [Fact]
    [Trait("Category", "Security")]
    public void CreateOptions_Always_DisablesContentLogging()
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();

        Assert.False(options.Diagnostics.IsLoggingContentEnabled);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CreateOptions_Always_KeepsChallengeResourceVerification()
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();

        Assert.False(options.DisableChallengeResourceVerification);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void CreateOptions_Always_DoesNotLogAuthorizationHeader()
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();

        Assert.DoesNotContain(options.Diagnostics.LoggedHeaderNames, name => string.Equals(name, "Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(options.Diagnostics.LoggedQueryParameters, name => string.Equals(name, "sig", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CreateOptions_Always_UsesTenSecondTimeoutAndThreeRetries()
    {
        var options = KeyVaultCryptographyClientFactory.CreateOptions();

        Assert.Equal(ExpectedRetries, options.Retry.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(ExpectedTimeoutSeconds), options.Retry.NetworkTimeout);
        Assert.Equal(KeyVaultCryptographyClientFactory.NetworkTimeout, options.Retry.NetworkTimeout);
    }

    [Fact]
    public void GetClient_SameKeyIdTwice_ReturnsSameInstance()
    {
        var factory = CreateFactory();
        var keyId = new Uri(KeyIdPrefix + "abc");

        Assert.Same(factory.GetClient(keyId), factory.GetClient(keyId));
    }

    [Fact]
    public void GetClient_KeyIdsDifferingOnlyInCase_ReturnSameInstance()
    {
        var factory = CreateFactory();

        var lower = factory.GetClient(new Uri(KeyIdPrefix + "abc"));
        var upper = factory.GetClient(new Uri(KeyIdPrefix + "ABC"));

        Assert.Same(lower, upper);
    }

    [Fact]
    public void GetClient_DifferentVersions_ReturnDifferentInstances()
    {
        var factory = CreateFactory();

        Assert.NotSame(factory.GetClient(new Uri(KeyIdPrefix + "one")), factory.GetClient(new Uri(KeyIdPrefix + "two")));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GetClient_MoreThanCacheLimit_StopsCachingNewClients()
    {
        var factory = CreateFactory();
        var cached = Enumerable.Range(0, CacheLimit)
            .Select(index => (Uri: new Uri(KeyIdPrefix + $"v{index}"), Client: factory.GetClient(new Uri(KeyIdPrefix + $"v{index}"))))
            .ToList();

        var overflowUri = new Uri(KeyIdPrefix + "overflow");
        var first = factory.GetClient(overflowUri);
        var second = factory.GetClient(overflowUri);

        Assert.NotSame(first, second);
        Assert.All(cached, entry => Assert.Same(entry.Client, factory.GetClient(entry.Uri)));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GetClient_ExactlyCacheLimitDistinctIds_CachesAll()
    {
        var factory = CreateFactory();
        var uris = Enumerable.Range(0, CacheLimit).Select(index => new Uri(KeyIdPrefix + $"v{index}")).ToList();
        var clients = uris.Select(factory.GetClient).ToList();

        for (var index = 0; index < uris.Count; index++)
        {
            Assert.Same(clients[index], factory.GetClient(uris[index]));
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GetClient_FloodOfDistinctVersions_NeverReturnsNull()
    {
        var factory = CreateFactory();

        for (var index = 0; index < CacheLimit * 4; index++)
        {
            Assert.NotNull(factory.GetClient(new Uri(KeyIdPrefix + $"flood{index}")));
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void GetClient_Constructed_MakesNoNetworkCall()
    {
        using var handler = new FakeKeyVaultHttpHandler();
        var factory = handler.CreateFactory();

        _ = factory.GetClient(new Uri(KeyIdPrefix + "abc"));
        _ = factory.GetClient(new Uri(FakeKeyVault.VersionlessKeyId));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void GetClient_NullKeyId_ThrowsArgumentNullException()
    {
        var factory = CreateFactory();

        Assert.Throws<ArgumentNullException>(() => factory.GetClient(null!));
    }

    private static KeyVaultCryptographyClientFactory CreateFactory() =>
        new(new StaticTokenCredential(), KeyVaultCryptographyClientFactory.CreateOptions());
}
