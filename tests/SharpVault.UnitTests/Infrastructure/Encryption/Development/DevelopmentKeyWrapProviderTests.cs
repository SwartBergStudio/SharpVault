// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Exceptions;
using SharpVault.Domain.Encryption;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Encryption.Development;

public sealed class DevelopmentKeyWrapProviderTests
{
    private const string NotAllowedMessage = "The development key wrap provider can only run in the Development environment.";
    private const string MaterialMissingMessage = "The development key wrap provider needs Encryption:DevelopmentKeyMaterial with at least 32 characters.";
    private const string InvalidDataKeyLengthMessage = "Data keys must be 32 bytes.";
    private const string DevelopmentEnvironment = "Development";
    private const int WrappedLength = 60;

    public static TheoryData<int> EveryWrappedByteIndex
    {
        get
        {
            var data = new TheoryData<int>();
            for (var index = 0; index < WrappedLength; index++)
            {
                data.Add(index);
            }

            return data;
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    [InlineData("Dev")]
    [InlineData("Development2")]
    [InlineData("")]
    [InlineData("development ")]
    [InlineData(" Development")]
    [InlineData("Development ")]
    [InlineData("Developmen")]
    [InlineData("Development\0")]
    [InlineData("Development\n")]
    public void Constructor_NonDevelopmentEnvironment_Throws(string environmentName)
    {
        var options = CreateOptions(RandomMaterial());

        var exception = Assert.Throws<InvalidOperationException>(
            () => new DevelopmentKeyWrapProvider(HostEnvironments.Named(environmentName), options));

        Assert.Equal(NotAllowedMessage, exception.Message);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData("development ")]
    public void Constructor_NonDevelopmentEnvironmentWithoutMaterial_ThrowsEnvironmentRefusalFirst(string environmentName)
    {
        var options = CreateOptions(null);

        var exception = Assert.Throws<InvalidOperationException>(
            () => new DevelopmentKeyWrapProvider(HostEnvironments.Named(environmentName), options));

        Assert.Equal(NotAllowedMessage, exception.Message);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Constructor_NonDevelopmentEnvironment_DoesNotEchoKeyMaterial()
    {
        var material = RandomMaterial();

        var exception = Assert.Throws<InvalidOperationException>(
            () => new DevelopmentKeyWrapProvider(HostEnvironments.Named("Production"), CreateOptions(material)));

        Assert.DoesNotContain(material, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("development")]
    [InlineData("DEVELOPMENT")]
    public void Constructor_DevelopmentEnvironmentInAnyCase_Succeeds(string environmentName)
    {
        using var provider = new DevelopmentKeyWrapProvider(HostEnvironments.Named(environmentName), CreateOptions(RandomMaterial()));

        Assert.NotNull(provider);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Constructor_NullEnvironment_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DevelopmentKeyWrapProvider(null!, CreateOptions(RandomMaterial())));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DevelopmentKeyWrapProvider(HostEnvironments.Named(DevelopmentEnvironment), null!));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("                                        ")]
    [InlineData("0123456789012345678901234567890")]
    [InlineData("  0123456789012345678901234567890  ")]
    [InlineData("\t0123456789012345678901234567890\n")]
    [InlineData("                0123456789012345                ")]
    public void Constructor_MissingOrShortKeyMaterial_ThrowsWithoutEchoingIt(string? material)
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new DevelopmentKeyWrapProvider(HostEnvironments.Named(DevelopmentEnvironment), CreateOptions(material)));

        Assert.Equal(MaterialMissingMessage, exception.Message);
        if (!string.IsNullOrWhiteSpace(material))
        {
            Assert.DoesNotContain(material.Trim(), exception.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Constructor_KeyMaterialOfThirtyTwoCharacters_Succeeds()
    {
        using var provider = new DevelopmentKeyWrapProvider(
            HostEnvironments.Named(DevelopmentEnvironment),
            CreateOptions(new string('k', 32)));

        Assert.NotNull(provider);
    }

    [Fact]
    public void Constructor_ThirtyTwoCharactersAfterTrimming_Succeeds()
    {
        using var provider = new DevelopmentKeyWrapProvider(
            HostEnvironments.Named(DevelopmentEnvironment),
            CreateOptions("  " + new string('k', 32) + "  "));

        Assert.NotNull(provider);
    }

    [Fact]
    public void KeyId_SameKeyMaterial_IsStable()
    {
        var material = RandomMaterial();
        using var first = CreateProvider(material);
        using var second = CreateProvider(material);

        Assert.Equal(first.KeyId, second.KeyId);
    }

    [Fact]
    public void KeyId_SameKeyMaterialWithSurroundingWhitespace_IsStable()
    {
        var material = RandomMaterial();
        using var first = CreateProvider(material);
        using var second = CreateProvider("  " + material + "\n");

        Assert.Equal(first.KeyId, second.KeyId);
    }

    [Fact]
    public void KeyId_DifferentKeyMaterial_Differs()
    {
        using var first = CreateProvider(RandomMaterial());
        using var second = CreateProvider(RandomMaterial());

        Assert.NotEqual(first.KeyId, second.KeyId);
    }

    [Fact]
    public void KeyId_Format_IsPrefixAndSixteenLowercaseHexCharacters()
    {
        using var provider = CreateProvider(RandomMaterial());

        Assert.Matches(new Regex("^development:[0-9a-f]{16}$", RegexOptions.None, TimeSpan.FromSeconds(1)), provider.KeyId);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void KeyId_Always_DoesNotContainKeyMaterial()
    {
        var material = RandomMaterial();
        using var provider = CreateProvider(material);

        Assert.DoesNotContain(material, provider.KeyId, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexStringLower(System.Text.Encoding.UTF8.GetBytes(material)), provider.KeyId, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrapThenUnwrap_Always_ReturnsOriginalDataKey()
    {
        using var provider = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();

        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        var unwrapped = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(dataKey, unwrapped);
    }

    [Fact]
    public async Task WrapThenUnwrap_ProviderRecreatedWithSameMaterial_ReturnsOriginalDataKey()
    {
        var material = RandomMaterial();
        var dataKey = RandomDataKey();
        using var first = CreateProvider(material);
        var wrapped = await first.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        using var second = CreateProvider(material);

        var unwrapped = await second.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(dataKey, unwrapped);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrapAsync_SameDataKeyTwice_ProducesDifferentWrappedValues()
    {
        using var provider = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();

        var first = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        var second = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);

        Assert.NotEqual(first.Value, second.Value);
        Assert.NotEqual(first.Value.AsSpan(0, 12).ToArray(), second.Value.AsSpan(0, 12).ToArray());
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrapAsync_Output_DoesNotContainPlaintextDataKey()
    {
        using var provider = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();

        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);

        Assert.False(wrapped.Value.AsSpan().IndexOf(dataKey) >= 0);
    }

    [Fact]
    public async Task WrapAsync_Output_IsSixtyBytesWithDevelopmentAlgorithmAndKeyId()
    {
        using var provider = CreateProvider(RandomMaterial());

        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);

        Assert.Equal(WrappedLength, wrapped.Value.Length);
        Assert.Equal("development-A256GCM", wrapped.Algorithm);
        Assert.Equal(provider.KeyId, wrapped.KeyId);
    }

    [Fact]
    public async Task WrapAsync_Always_DoesNotModifyOrKeepTheCallersDataKey()
    {
        using var provider = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();
        var copy = dataKey.ToArray();

        await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);

        Assert.Equal(copy, dataKey);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public async Task WrapAsync_DataKeyNot32Bytes_Throws(int length)
    {
        using var provider = CreateProvider(RandomMaterial());

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.WrapAsync(new byte[length], TestContext.Current.CancellationToken));

        Assert.StartsWith(InvalidDataKeyLengthMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrapAsync_NullDataKey_ThrowsArgumentNullException()
    {
        using var provider = CreateProvider(RandomMaterial());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.WrapAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_WrappedByOtherKeyMaterial_ThrowsKeyMismatch()
    {
        using var other = CreateProvider(RandomMaterial());
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await other.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_KeyIdRewrittenToThisProvider_ThrowsAuthenticationFailed()
    {
        using var other = CreateProvider(RandomMaterial());
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await other.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var rewritten = wrapped with { KeyId = provider.KeyId };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(rewritten, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_KeyIdChangedAfterWrap_ThrowsKeyMismatch()
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var changed = wrapped with { KeyId = provider.KeyId.ToUpperInvariant() };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(changed, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_KeyVaultWrappedKey_ThrowsKeyMismatch()
    {
        using var provider = CreateProvider(RandomMaterial());
        var foreign = new WrappedDataKey("https://contoso.vault.azure.net/keys/sharpvault-kek/0f1e2d3c4b5a69788796a5b4c3d2e1f0", "RSA-OAEP-256", new byte[256]);

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(foreign, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("RSA-OAEP-256")]
    [InlineData("development-a256gcm")]
    [InlineData("")]
    [InlineData("development-A256GCM ")]
    public async Task UnwrapAsync_AlgorithmChanged_ThrowsKeyMismatch(string algorithm)
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var changed = wrapped with { Algorithm = algorithm };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(changed, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_NullAlgorithm_ThrowsKeyMismatch()
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var changed = wrapped with { Algorithm = null! };

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(changed, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.KeyMismatch, exception.Reason);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(EveryWrappedByteIndex))]
    public async Task UnwrapAsync_AnyByteFlipped_ThrowsAuthenticationFailed(int index)
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[index] ^= 0x01;

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0x80)]
    [InlineData(0xFF)]
    public async Task UnwrapAsync_EveryBitOfEveryByteFlipped_NeverSucceeds(int mask)
    {
        using var provider = CreateProvider(RandomMaterial());
        var original = RandomDataKey();
        var wrapped = await provider.WrapAsync(original, TestContext.Current.CancellationToken);

        for (var index = 0; index < WrappedLength; index++)
        {
            var tampered = wrapped.Value.ToArray();
            tampered[index] ^= (byte)mask;

            await Assert.ThrowsAsync<SecretDecryptionException>(
                () => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken));
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(59)]
    [InlineData(61)]
    [InlineData(256)]
    public async Task UnwrapAsync_WrongLength_ThrowsMalformedWrappedKey(int length)
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var resized = new byte[length];
        wrapped.Value.AsSpan(0, Math.Min(length, WrappedLength)).CopyTo(resized);

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = resized }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_NullValue_ThrowsMalformedWrappedKey()
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped with { Value = null! }, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.MalformedWrappedKey, exception.Reason);
    }

    [Fact]
    public async Task UnwrapAsync_NullWrappedKey_ThrowsArgumentNullException()
    {
        using var provider = CreateProvider(RandomMaterial());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => provider.UnwrapAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task UnwrapAsync_TwoCalls_ReturnDifferentArrays()
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);

        var first = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);
        var second = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.NotSame(first, second);
        Assert.Equal(first, second);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_ZeroingTheReturnedArray_DoesNotAffectLaterUnwraps()
    {
        using var provider = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        var first = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);
        CryptographicOperations.ZeroMemory(first);

        var second = await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(dataKey, second);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task UnwrapAsync_WrappedKeyValue_IsNotModified()
    {
        using var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        var copy = wrapped.Value.ToArray();

        await provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken);

        Assert.Equal(copy, wrapped.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_ThenUnwrapPreviouslyWrappedKey_ThrowsAuthenticationFailed()
    {
        var provider = CreateProvider(RandomMaterial());
        var wrapped = await provider.WrapAsync(RandomDataKey(), TestContext.Current.CancellationToken);
        provider.Dispose();

        var exception = await Assert.ThrowsAsync<SecretDecryptionException>(
            () => provider.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));

        Assert.Equal(DecryptionFailureReason.AuthenticationFailed, exception.Reason);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Dispose_Always_ZeroesTheKeyEncryptionKey()
    {
        var provider = CreateProvider(RandomMaterial());
        var field = typeof(DevelopmentKeyWrapProvider).GetField("_keyEncryptionKey", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var keyEncryptionKey = Assert.IsType<byte[]>(field.GetValue(provider));
        Assert.Contains(keyEncryptionKey, static value => value != 0);

        provider.Dispose();

        Assert.All(keyEncryptionKey, static value => Assert.Equal(0, value));
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var provider = CreateProvider(RandomMaterial());

        provider.Dispose();
        var exception = Record.Exception(provider.Dispose);

        Assert.Null(exception);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Dispose_ThenWrapAndUnwrapAgain_DoesNotRoundTripThroughANewlyWrappedKey()
    {
        var material = RandomMaterial();
        var provider = CreateProvider(material);
        var dataKey = RandomDataKey();
        provider.Dispose();

        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        using var fresh = CreateProvider(material);

        await Assert.ThrowsAsync<SecretDecryptionException>(
            () => fresh.UnwrapAsync(wrapped, TestContext.Current.CancellationToken));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DevelopmentKeyWrapProvider_ToString_ContainsNoKeyMaterial()
    {
        var material = RandomMaterial();
        using var provider = CreateProvider(material);

        var text = provider.ToString();

        Assert.DoesNotContain(material, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(material)), text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Exceptions_Always_ContainNeitherKeyMaterialNorDataKey()
    {
        var material = RandomMaterial();
        using var provider = CreateProvider(material);
        using var other = CreateProvider(RandomMaterial());
        var dataKey = RandomDataKey();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);
        var tampered = wrapped.Value.ToArray();
        tampered[20] ^= 0x01;
        var failures = new List<Exception>
        {
            await Assert.ThrowsAsync<SecretDecryptionException>(() => other.UnwrapAsync(wrapped, TestContext.Current.CancellationToken)),
            await Assert.ThrowsAsync<SecretDecryptionException>(() => provider.UnwrapAsync(wrapped with { Value = tampered }, TestContext.Current.CancellationToken)),
            await Assert.ThrowsAsync<SecretDecryptionException>(() => provider.UnwrapAsync(wrapped with { Value = new byte[3] }, TestContext.Current.CancellationToken)),
            await Assert.ThrowsAsync<ArgumentException>(() => provider.WrapAsync(dataKey.AsSpan(0, 31).ToArray(), TestContext.Current.CancellationToken)),
        };

        foreach (var failure in failures)
        {
            var text = failure.ToString();
            Assert.DoesNotContain(material, text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToBase64String(dataKey), text, StringComparison.Ordinal);
            Assert.DoesNotContain(Convert.ToHexString(dataKey), text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Convert.ToBase64String(wrapped.Value), text, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrappedDataKey_ToString_ContainsNoKeyMaterialOrWrappedBytes()
    {
        var material = RandomMaterial();
        using var provider = CreateProvider(material);
        var dataKey = RandomDataKey();
        var wrapped = await provider.WrapAsync(dataKey, TestContext.Current.CancellationToken);

        var text = wrapped.ToString();

        Assert.DoesNotContain(material, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(wrapped.Value), text, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(wrapped.Value), text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Convert.ToBase64String(dataKey), text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task WrapAndUnwrap_InParallel_AllRoundTrip()
    {
        using var provider = CreateProvider(RandomMaterial());
        var cancellationToken = TestContext.Current.CancellationToken;

        var results = await Task.WhenAll(Enumerable.Range(0, 1000).Select(async _ =>
        {
            var dataKey = RandomDataKey();
            var wrapped = await provider.WrapAsync(dataKey, cancellationToken);
            var unwrapped = await provider.UnwrapAsync(wrapped, cancellationToken);
            return dataKey.AsSpan().SequenceEqual(unwrapped);
        }));

        Assert.All(results, Assert.True);
    }

    private static DevelopmentKeyWrapProvider CreateProvider(string material) =>
        new(HostEnvironments.Named(DevelopmentEnvironment), CreateOptions(material));

    private static IOptions<EncryptionOptions> CreateOptions(string? material) =>
        Options.Create(new EncryptionOptions { DevelopmentKeyMaterial = material });

    private static string RandomMaterial() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static byte[] RandomDataKey() => RandomNumberGenerator.GetBytes(32);
}
