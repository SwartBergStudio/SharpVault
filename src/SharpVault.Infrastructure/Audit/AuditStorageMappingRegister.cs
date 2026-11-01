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
            .Ignore(d => d.ETag)
            .Ignore(d => d.Timestamp!);
    }
}
