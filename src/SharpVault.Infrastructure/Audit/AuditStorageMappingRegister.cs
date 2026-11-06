// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Mapster;
using SharpVault.Domain.Audit;

namespace SharpVault.Infrastructure.Audit;

public sealed class AuditStorageMappingRegister : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<AuditEntry, AuditLogTableEntity>()
            .Map(d => d.PartitionKey, s => AuditKeys.PartitionKey(s.OccurredAt))
            .Map(d => d.RowKey, s => AuditKeys.RowKey(s.OccurredAt, s.Id))
            .Map(d => d.EventType, s => s.EventType.ToString())
            .Map(d => d.ActorId, s => s.ActorId.ToString("D"))
            .Map(d => d.OwnerId, s => s.OwnerId.HasValue ? s.OwnerId.Value.ToString("D") : null)
            .Map(d => d.SecretId, s => s.SecretId.HasValue ? s.SecretId.Value.ToString("D") : null)
            .Map(d => d.RecipientId, s => s.RecipientId.HasValue ? s.RecipientId.Value.ToString("D") : null)
            .Map(d => d.TransferId, s => s.TransferId.HasValue ? s.TransferId.Value.ToString("D") : null)
            .Map(d => d.ReasonCategory, s => s.ReasonCategory)
            .Ignore(d => d.ETag)
            .Ignore(d => d.Timestamp!);

        config.NewConfig<AuditEntry, SecretAuditLogTableEntity>()
            .Map(d => d.PartitionKey, s => SecretAuditKeys.PartitionKey(s.OwnerId!.Value))
            .Map(d => d.RowKey, s => SecretAuditKeys.RowKey(s.SecretId!.Value, s.OccurredAt, s.Id))
            .Map(d => d.EventType, s => s.EventType.ToString())
            .Map(d => d.ActorId, s => s.ActorId.ToString("D"))
            .Map(d => d.OwnerId, s => s.OwnerId.HasValue ? s.OwnerId.Value.ToString("D") : null)
            .Map(d => d.SecretId, s => s.SecretId.HasValue ? s.SecretId.Value.ToString("D") : null)
            .Map(d => d.RecipientId, s => s.RecipientId.HasValue ? s.RecipientId.Value.ToString("D") : null)
            .Map(d => d.TransferId, s => s.TransferId.HasValue ? s.TransferId.Value.ToString("D") : null)
            .Map(d => d.ReasonCategory, s => s.ReasonCategory)
            .Ignore(d => d.ETag)
            .Ignore(d => d.Timestamp!);
    }
}
