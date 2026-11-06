// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;
using SharpVault.UnitTests.Support.Encryption;
using SharpVault.UnitTests.Support.Vault;

namespace SharpVault.UnitTests.Application.Vault;

public sealed class SecretMetadataCipherTests
{
    private const int Version = VaultLimits.CurrentMetadataKeyVersion;

    private readonly EncryptionHarness _harness = new();
    private readonly Guid _owner = Guid.NewGuid();

    private Secret NewSecret() => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = _owner,
        Type = SecretType.Login,
        DataKey = VaultSamples.Key(),
        MetadataKeyVersion = Version,
        Revision = 1,
        CreatedAt = VaultSamples.Created,
        ModifiedAt = VaultSamples.Created,
    };

    private static SecretMetadata Metadata() => new("  Prod DB  ", " admin ", " https://db.example.test/ ", [" prod ", "db"]);

    [Fact]
    public async Task Encrypt_ThenTryDecrypt_RoundTripsAllFields()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();

        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        var ok = SecretMetadataCipher.TryDecrypt(session, secret, Version, out var metadata);

        Assert.True(ok);
        Assert.NotNull(metadata);
        Assert.Equal("Prod DB", metadata.Name);
        Assert.Equal("admin", metadata.Username);
        Assert.Equal("https://db.example.test/", metadata.Url);
        Assert.Equal(["prod", "db"], metadata.Tags);
    }

    [Fact]
    public async Task Encrypt_AbsentUsernameUrlAndTags_StoresNullEnvelopes()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();

        SecretMetadataCipher.Encrypt(session, secret, new SecretMetadata("Only name", null, null, []));
        var ok = SecretMetadataCipher.TryDecrypt(session, secret, Version, out var metadata);

        Assert.Null(secret.UsernameEnvelope);
        Assert.Null(secret.UrlEnvelope);
        Assert.Null(secret.TagsEnvelope);
        Assert.NotEmpty(secret.NameEnvelope);
        Assert.True(ok);
        Assert.Null(metadata!.Username);
        Assert.Null(metadata.Url);
        Assert.Empty(metadata.Tags);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Encrypt_BlankUsernameAndUrl_AreStoredAsNull(string blank)
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();

        SecretMetadataCipher.Encrypt(session, secret, new SecretMetadata("Name", blank, blank, []));

        Assert.Null(secret.UsernameEnvelope);
        Assert.Null(secret.UrlEnvelope);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task Encrypt_Envelopes_DoNotContainThePlaintext()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        var metadata = new SecretMetadata("SV-NAME-7731", "SV-USER-7731", "https://sv-url-7731.test/", ["SV-TAG-7731"]);

        SecretMetadataCipher.Encrypt(session, secret, metadata);

        foreach (var envelope in new[] { secret.NameEnvelope, secret.UsernameEnvelope!, secret.UrlEnvelope!, secret.TagsEnvelope! })
        {
            Assert.DoesNotContain("7731", Encoding.UTF8.GetString(envelope), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecrypt_NameEnvelopeFromOtherSecret_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var first = NewSecret();
        var second = NewSecret();
        SecretMetadataCipher.Encrypt(session, first, Metadata());
        SecretMetadataCipher.Encrypt(session, second, Metadata());
        first.NameEnvelope = second.NameEnvelope;

        Assert.False(SecretMetadataCipher.TryDecrypt(session, first, Version, out var metadata));
        Assert.Null(metadata);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecrypt_SwappedNameAndUsername_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        (secret.NameEnvelope, secret.UsernameEnvelope) = (secret.UsernameEnvelope!, secret.NameEnvelope);

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecrypt_WrongOwner_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        secret.OwnerId = Guid.NewGuid();

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecrypt_OtherOwnersKey_ReturnsFalse()
    {
        using var mine = await _harness.CreateSessionAsync();
        using var theirs = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(mine, secret, Metadata());

        Assert.False(SecretMetadataCipher.TryDecrypt(theirs, secret, Version, out _));
    }

    [Fact]
    public async Task TryDecrypt_VersionMismatch_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version + 1, out _));
        secret.MetadataKeyVersion = Version + 1;
        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    public async Task TryDecrypt_TagsNotJson_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        secret.TagsEnvelope = session.Encrypt(new EncryptionContext(VaultPurposes.SecretTags, secret.Id, secret.OwnerId), "not json at all");

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[1,2]")]
    [InlineData("{\"a\":1}")]
    [InlineData("\"text\"")]
    public async Task TryDecrypt_TagsJsonOfTheWrongShape_ReturnsFalse(string json)
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        secret.TagsEnvelope = session.Encrypt(new EncryptionContext(VaultPurposes.SecretTags, secret.Id, secret.OwnerId), json);

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecrypt_TamperedEnvelope_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        secret.UrlEnvelope![^1] ^= 0x01;

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    public async Task TryDecrypt_GarbageEnvelope_ReturnsFalseInsteadOfThrowing()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        secret.NameEnvelope = [1, 2, 3];

        Assert.False(SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }

    [Fact]
    public async Task EncryptFolderName_RoundTrips()
    {
        using var session = await _harness.CreateSessionAsync();
        var folder = VaultSamples.Folder(_owner);

        SecretMetadataCipher.EncryptFolderName(session, folder, "  Finance  ");
        var ok = SecretMetadataCipher.TryDecryptFolderName(session, folder, Version, out var name);

        Assert.True(ok);
        Assert.Equal("Finance", name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecryptFolderName_FromOtherFolder_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var first = VaultSamples.Folder(_owner);
        var second = VaultSamples.Folder(_owner);
        SecretMetadataCipher.EncryptFolderName(session, first, "First");
        SecretMetadataCipher.EncryptFolderName(session, second, "Second");
        first.NameEnvelope = second.NameEnvelope;

        Assert.False(SecretMetadataCipher.TryDecryptFolderName(session, first, Version, out var name));
        Assert.Null(name);
    }

    [Fact]
    public async Task TryDecryptFolderName_VersionMismatch_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var folder = VaultSamples.Folder(_owner);
        SecretMetadataCipher.EncryptFolderName(session, folder, "Finance");

        Assert.False(SecretMetadataCipher.TryDecryptFolderName(session, folder, Version + 1, out _));
    }

    [Fact]
    [Trait("Category", "Security")]
    public async Task TryDecryptFolderName_SecretNameEnvelope_ReturnsFalse()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        var folder = VaultSamples.Folder(_owner, secret.Id);
        folder.NameEnvelope = secret.NameEnvelope;

        Assert.False(SecretMetadataCipher.TryDecryptFolderName(session, folder, Version, out _));
    }

    [Fact]
    public async Task EncryptFolderName_BlankName_Throws()
    {
        using var session = await _harness.CreateSessionAsync();

        Assert.ThrowsAny<ArgumentException>(() => SecretMetadataCipher.EncryptFolderName(session, VaultSamples.Folder(_owner), "  "));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();

        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.Encrypt(null!, secret, Metadata()));
        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.Encrypt(session, null!, Metadata()));
        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.Encrypt(session, secret, null!));
        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.TryDecrypt(null!, secret, Version, out _));
        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.TryDecrypt(session, null!, Version, out _));
        Assert.Throws<ArgumentNullException>(() => SecretMetadataCipher.TryDecryptFolderName(session, null!, Version, out _));
    }

    [Fact]
    public async Task TryDecrypt_DisposedSession_Propagates()
    {
        using var session = await _harness.CreateSessionAsync();
        var secret = NewSecret();
        SecretMetadataCipher.Encrypt(session, secret, Metadata());
        session.Dispose();

        Assert.Throws<ObjectDisposedException>(() => SecretMetadataCipher.TryDecrypt(session, secret, Version, out _));
    }
}
