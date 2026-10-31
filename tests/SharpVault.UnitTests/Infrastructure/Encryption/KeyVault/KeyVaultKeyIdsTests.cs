// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Security.KeyVault.Keys;
using SharpVault.Infrastructure.Encryption.KeyVault;

namespace SharpVault.UnitTests.Infrastructure.Encryption.KeyVault;

[Trait("Category", "Security")]
public sealed class KeyVaultKeyIdsTests
{
    private const string Vault = "https://x.vault.azure.net";
    private const string ConfiguredKeyId = Vault + "/keys/k";
    private const string Version = "0123abcd";
    private const int OverlongLength = 513;

    public static TheoryData<string> ValidConfiguredKeyIds => new()
    {
        "https://x.vault.azure.net/keys/k",
        "https://x.vault.azure.net/keys/k/",
        "https://x.managedhsm.azure.net/keys/k",
        "https://x.vault.azure.cn/keys/k",
        "https://x.vault.azure.net:8443/keys/k",
    };

    public static TheoryData<string> InvalidConfiguredKeyIds => new()
    {
        string.Empty,
        "   ",
        "not a uri",
        "/keys/k",
        "http://x.vault.azure.net/keys/k",
        "https://x.vault.azure.net/keys/k/0123abcd",
        "https://x.vault.azure.net/secrets/k",
        "https://x.vault.azure.net/certificates/k",
        "https://x.vault.azure.net//keys/k",
        "https://x.vault.azure.net/keys",
        "https://x.vault.azure.net/keys/",
        "https://x.vault.azure.net/keys/k/v/extra",
        "https://user:pw@x.vault.azure.net/keys/k",
        "https://x.vault.azure.net/keys/k?sig=abc",
        "https://x.vault.azure.net/keys/k#f",
        "ftp://x.vault.azure.net/keys/k",
        "development:0123456789abcdef",
        PaddedTo(ConfiguredKeyId, 'k', OverlongLength),
    };

    public static TheoryData<string> VersionsOfConfiguredKey => new()
    {
        ConfiguredKeyId + "/" + Version,
        "https://X.VAULT.AZURE.NET/keys/k/" + Version,
        "https://x.vault.azure.net/keys/K/" + Version,
        "HTTPS://x.vault.azure.net/keys/k/" + Version,
    };

    public static TheoryData<string> NotVersionsOfConfiguredKey => new()
    {
        "https://y.vault.azure.net/keys/k/" + Version,
        "https://x.vault.azure.net.evil.example/keys/k/" + Version,
        "https://evilx.vault.azure.net/keys/k/" + Version,
        "https://x.vault.azure.net/keys/other/" + Version,
        "https://x.vault.azure.net/keys/kk/" + Version,
        ConfiguredKeyId,
        ConfiguredKeyId + "/",
        "http://x.vault.azure.net/keys/k/" + Version,
        "https://x.vault.azure.net:8443/keys/k/" + Version,
        "https://x.vault.azure.net/secrets/k/" + Version,
        "https://x.vault.azure.net/certificates/k/" + Version,
        "https://x.vault.azure.net//keys/k/" + Version,
        "https://x.vault.azure.net/keys/k/" + Version + "?x=1",
        "https://x.vault.azure.net/keys/k/" + Version + "#f",
        "https://user:pw@x.vault.azure.net/keys/k/" + Version,
        "https://x.vault.azure.net/keys/k/" + Version + "/extra",
        "development:0123456789abcdef",
        string.Empty,
        "   ",
        PaddedTo(ConfiguredKeyId + "/", 'a', OverlongLength),
    };

    [Theory]
    [MemberData(nameof(ValidConfiguredKeyIds))]
    public void TryParseConfigured_VersionlessHttpsKeyId_ReturnsTrue(string keyId)
    {
        var parsed = KeyVaultKeyIds.TryParseConfigured(keyId, out var identifier);

        Assert.True(parsed);
        Assert.NotNull(identifier);
        Assert.Null(identifier.Value.Version);
    }

    [Theory]
    [InlineData(null)]
    [MemberData(nameof(InvalidConfiguredKeyIds))]
    public void TryParseConfigured_InvalidKeyId_ReturnsFalse(string? keyId)
    {
        var parsed = KeyVaultKeyIds.TryParseConfigured(keyId, out var identifier);

        Assert.False(parsed);
        Assert.Null(identifier);
    }

    [Fact]
    public void TryParseConfigured_ExactlyMaximumLength_ReturnsTrue()
    {
        var keyId = PaddedTo(ConfiguredKeyId, 'k', KeyVaultKeyIds.MaxKeyIdLength);

        Assert.Equal(KeyVaultKeyIds.MaxKeyIdLength, keyId.Length);
        Assert.True(KeyVaultKeyIds.TryParseConfigured(keyId, out _));
    }

    [Theory]
    [MemberData(nameof(VersionsOfConfiguredKey))]
    public void IsVersionOf_SameVaultAndNameWithVersion_ReturnsTrue(string keyId)
    {
        Assert.True(KeyVaultKeyIds.IsVersionOf(keyId, Configured()));
    }

    [Theory]
    [InlineData(null)]
    [MemberData(nameof(NotVersionsOfConfiguredKey))]
    public void IsVersionOf_Mismatch_ReturnsFalse(string? keyId)
    {
        Assert.False(KeyVaultKeyIds.IsVersionOf(keyId, Configured()));
    }

    private static KeyVaultKeyIdentifier Configured()
    {
        Assert.True(KeyVaultKeyIds.TryParseConfigured(ConfiguredKeyId, out var configured));
        return configured.Value;
    }

    private static string PaddedTo(string prefix, char fill, int length) =>
        prefix + new string(fill, length - prefix.Length);
}
