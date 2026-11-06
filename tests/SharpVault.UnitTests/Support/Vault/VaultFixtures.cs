// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using AutoFixture;
using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Features.Vault.Common;
using SharpVault.Domain.Vaults;

namespace SharpVault.UnitTests.Support.Vault;

internal static class VaultFixtures
{
    public static IFixture Customize(this IFixture fixture, IDataKeySession metadataSession, IDataKeySession secretSession, Guid ownerId)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(metadataSession);
        ArgumentNullException.ThrowIfNull(secretSession);

        fixture.Customize<Secret>(composer => composer.FromFactory(() => NewSecret(fixture, metadataSession, secretSession, ownerId)).OmitAutoProperties());
        fixture.Customize<Folder>(composer => composer.FromFactory(() => NewFolder(fixture, metadataSession, ownerId)).OmitAutoProperties());
        fixture.Customize<SecretVersion>(composer => composer.FromFactory(() => NewVersion(fixture, secretSession, ownerId)).OmitAutoProperties());
        return fixture;
    }

    private static Secret NewSecret(IFixture fixture, IDataKeySession metadataSession, IDataKeySession secretSession, Guid ownerId)
    {
        var secret = new Secret
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Type = SecretType.Login,
            DataKey = secretSession.WrappedKey,
            MetadataKeyVersion = VaultLimits.CurrentMetadataKeyVersion,
            Revision = 1,
            CreatedAt = VaultSamples.Created,
            ModifiedAt = VaultSamples.Created,
        };
        var metadata = new SecretMetadata(
            "name-" + fixture.Create<Guid>().ToString("N"),
            "user-" + fixture.Create<Guid>().ToString("N"),
            "https://" + fixture.Create<Guid>().ToString("N") + ".test/",
            ["tag-" + fixture.Create<Guid>().ToString("N")[..8]]);
        SecretMetadataCipher.Encrypt(metadataSession, secret, metadata);
        secret.ValueEnvelope = secretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValue, secret.Id, ownerId), "value-" + fixture.Create<Guid>().ToString("N"));
        secret.NotesEnvelope = secretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretNotes, secret.Id, ownerId), "notes-" + fixture.Create<Guid>().ToString("N"));
        return secret;
    }

    private static Folder NewFolder(IFixture fixture, IDataKeySession metadataSession, Guid ownerId)
    {
        var folder = new Folder
        {
            Id = Guid.CreateVersion7(),
            OwnerId = ownerId,
            MetadataKeyVersion = VaultLimits.CurrentMetadataKeyVersion,
            CreatedAt = VaultSamples.Created,
            ModifiedAt = VaultSamples.Created,
        };
        SecretMetadataCipher.EncryptFolderName(metadataSession, folder, "folder-" + fixture.Create<Guid>().ToString("N"));
        return folder;
    }

    private static SecretVersion NewVersion(IFixture fixture, IDataKeySession secretSession, Guid ownerId)
    {
        var version = new SecretVersion
        {
            Id = Guid.CreateVersion7(),
            SecretId = Guid.CreateVersion7(),
            OwnerId = ownerId,
            Revision = 1,
            ReplacedAt = VaultSamples.Created,
        };
        version.ValueEnvelope = secretSession.Encrypt(new EncryptionContext(VaultPurposes.SecretValueHistory, version.SecretId, ownerId), "old-" + fixture.Create<Guid>().ToString("N"));
        return version;
    }
}
