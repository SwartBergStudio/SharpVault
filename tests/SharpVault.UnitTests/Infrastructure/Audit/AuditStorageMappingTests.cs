// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Audit;
using SharpVault.Infrastructure.Audit;

namespace SharpVault.UnitTests.Infrastructure.Audit;

public sealed class AuditStorageMappingTests
{
    private static readonly string[] EntityProperties = ["ActorId", "ETag", "EventType", "IpAddress", "OccurredAt", "OwnerId", "PartitionKey", "Reason", "ReasonCategory", "RecipientId", "RowKey", "SecretId", "SharePermission", "TargetId", "Timestamp", "TransferId"];
    private static readonly string[] EntryProperties = ["ActorId", "EventType", "Id", "IpAddress", "OccurredAt", "OwnerId", "Reason", "ReasonCategory", "RecipientId", "SecretId", "SharePermission", "TargetId", "TransferId"];

    private static AuditEntry VaultEntry(Guid? owner, Guid? secret) => new()
    {
        Id = Guid.NewGuid(),
        EventType = AuditEventType.SecretValueRevealed,
        ActorId = Guid.NewGuid(),
        TargetId = secret?.ToString("D"),
        OccurredAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
        IpAddress = "10.0.0.2",
        OwnerId = owner,
        SecretId = secret,
    };

    private static TypeAdapterConfig ScannedConfig()
    {
        var config = new TypeAdapterConfig();
        config.Scan(typeof(AuditKeys).Assembly);
        return config;
    }

    [Fact]
    public void Adapt_AuditEntry_MapsKeysAndFields()
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventType.SignIn,
            ActorId = Guid.NewGuid(),
            TargetId = null,
            OccurredAt = new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            IpAddress = "10.0.0.1",
            Reason = null,
        };

        var entity = entry.Adapt<AuditLogTableEntity>(ScannedConfig());

        Assert.Equal(AuditKeys.PartitionKey(entry.OccurredAt), entity.PartitionKey);
        Assert.Equal(AuditKeys.RowKey(entry.OccurredAt, entry.Id), entity.RowKey);
        Assert.Equal("SignIn", entity.EventType);
        Assert.Equal(entry.ActorId.ToString("D"), entity.ActorId);
        Assert.Equal(entry.OccurredAt, entity.OccurredAt);
        Assert.Equal("10.0.0.1", entity.IpAddress);
        Assert.Null(entity.TargetId);
        Assert.Null(entity.Reason);
    }

    [Fact]
    public void Adapt_VaultAuditEntry_MapsOwnerAndSecretIds()
    {
        var owner = Guid.NewGuid();
        var secret = Guid.NewGuid();
        var entry = VaultEntry(owner, secret);

        var entity = entry.Adapt<AuditLogTableEntity>(ScannedConfig());

        Assert.Equal(owner.ToString("D"), entity.OwnerId);
        Assert.Equal(secret.ToString("D"), entity.SecretId);
        Assert.Equal(AuditKeys.PartitionKey(entry.OccurredAt), entity.PartitionKey);
        Assert.Equal(AuditKeys.RowKey(entry.OccurredAt, entry.Id), entity.RowKey);
    }

    [Fact]
    public void Adapt_ShareEntry_MapsRecipientAndPermissionToBothTables()
    {
        var recipient = Guid.NewGuid();
        var entry = VaultEntry(Guid.NewGuid(), Guid.NewGuid());
        entry.EventType = AuditEventType.SecretShared;
        entry.RecipientId = recipient;
        entry.SharePermission = "Edit";

        var general = entry.Adapt<AuditLogTableEntity>(ScannedConfig());
        var secretIndex = entry.Adapt<SecretAuditLogTableEntity>(ScannedConfig());

        Assert.Equal(recipient.ToString("D"), general.RecipientId);
        Assert.Equal("Edit", general.SharePermission);
        Assert.Equal(recipient.ToString("D"), secretIndex.RecipientId);
        Assert.Equal("Edit", secretIndex.SharePermission);
    }

    [Fact]
    public void Adapt_EntryWithoutOwnerAndSecret_LeavesBothColumnsNull()
    {
        var entry = VaultEntry(null, null);

        var entity = entry.Adapt<AuditLogTableEntity>(ScannedConfig());

        Assert.Null(entity.OwnerId);
        Assert.Null(entity.SecretId);
    }

    [Fact]
    public void Adapt_SecretAuditEntry_UsesOwnerPartitionAndSecretPrefixedRowKey()
    {
        var owner = Guid.NewGuid();
        var secret = Guid.NewGuid();
        var entry = VaultEntry(owner, secret);

        var entity = entry.Adapt<SecretAuditLogTableEntity>(ScannedConfig());

        Assert.Equal(SecretAuditKeys.PartitionKey(owner), entity.PartitionKey);
        Assert.Equal(owner.ToString("D"), entity.PartitionKey);
        Assert.Equal(SecretAuditKeys.RowKey(secret, entry.OccurredAt, entry.Id), entity.RowKey);
        Assert.StartsWith(secret.ToString("N") + "_", entity.RowKey, StringComparison.Ordinal);
        Assert.Equal("SecretValueRevealed", entity.EventType);
        Assert.Equal(entry.ActorId.ToString("D"), entity.ActorId);
        Assert.Equal(owner.ToString("D"), entity.OwnerId);
        Assert.Equal(secret.ToString("D"), entity.SecretId);
        Assert.Equal(entry.OccurredAt, entity.OccurredAt);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SecretAuditLogTableEntity_Properties_MatchTheAuditEntity()
    {
        var names = typeof(SecretAuditLogTableEntity).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(EntityProperties, names);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SecretAuditKeys_NewerEvents_SortFirstWithinASecret()
    {
        var secret = Guid.NewGuid();
        var older = SecretAuditKeys.RowKey(secret, new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero), Guid.NewGuid());
        var newer = SecretAuditKeys.RowKey(secret, new DateTimeOffset(2026, 3, 1, 9, 0, 1, TimeSpan.Zero), Guid.NewGuid());

        Assert.True(string.CompareOrdinal(newer, older) < 0);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditLogTableEntity_Properties_HaveNoNameEmailOrSecretFields()
    {
        var names = typeof(AuditLogTableEntity).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(EntityProperties, names);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void AuditEntry_Properties_HaveNoNameEmailOrSecretFields()
    {
        var names = typeof(AuditEntry).GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal);

        Assert.Equal(EntryProperties, names);
    }
}
