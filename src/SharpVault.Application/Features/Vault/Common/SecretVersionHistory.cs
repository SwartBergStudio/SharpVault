// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Encryption;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Features.Vault.Common;

public static class SecretVersionHistory
{
    public static IReadOnlyList<Versioned<SecretVersion>> NewestFirst(IEnumerable<Versioned<SecretVersion>> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        return [.. versions
            .OrderByDescending(version => version.Value.ReplacedAt)
            .ThenByDescending(version => version.Value.Id)];
    }

    public static List<VaultChange> ArchiveCurrentValue(
        IDataKeySession secretKey,
        Secret secret,
        IEnumerable<Versioned<SecretVersion>> existing,
        Guid versionId,
        DateTimeOffset now,
        Guid? keepVersionId = null)
    {
        ArgumentNullException.ThrowIfNull(secretKey);
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(existing);

        if (secret.ValueEnvelope is null)
        {
            return [];
        }

        var version = new SecretVersion
        {
            Id = versionId,
            SecretId = secret.Id,
            OwnerId = secret.OwnerId,
            ValueEnvelope = secretKey.ReEncrypt(
                new EncryptionContext(VaultPurposes.SecretValue, secret.Id, secret.OwnerId),
                new EncryptionContext(VaultPurposes.SecretValueHistory, versionId, secret.OwnerId),
                secret.ValueEnvelope),
            Revision = secret.Revision,
            ReplacedAt = now,
        };

        var changes = new List<VaultChange> { VaultChange.AddVersion(version) };
        var ordered = NewestFirst(existing);
        var surplusCount = ordered.Count + 1 - VaultLimits.HistoryVersionLimit;
        if (surplusCount > 0)
        {
            var oldest = ordered.Where(old => old.Value.Id != keepVersionId).TakeLast(surplusCount);
            changes.AddRange(oldest.Select(old => VaultChange.DeleteVersion(old.Value, old.ETag)));
        }

        return changes;
    }
}
