// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Audit;
using SharpVault.Infrastructure.Audit;

namespace SharpVault.UnitTests.Infrastructure.Audit;

public sealed class AuditStorageMappingTests
{
    private static readonly string[] EntityProperties = ["ActorId", "ETag", "EventType", "IpAddress", "OccurredAt", "PartitionKey", "Reason", "RowKey", "TargetId", "Timestamp"];
    private static readonly string[] EntryProperties = ["ActorId", "EventType", "Id", "IpAddress", "OccurredAt", "Reason", "TargetId"];

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
