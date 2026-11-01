// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Mapster;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Domain.Audit;
using SharpVault.Infrastructure.Storage;

namespace SharpVault.Infrastructure.Audit;

public sealed class TableAuditLog(TableServiceClient tableServiceClient) : IAuditLog
{
    private readonly TableClient _table = tableServiceClient.GetTableClient(TableNames.AuditLog);

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await _table.AddEntityAsync(entry.Adapt<AuditLogTableEntity>(), cancellationToken);
    }
}
