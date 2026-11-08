// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Azure.Data.Tables;

namespace SharpVault.E2ETests.Support;

public static class E2EAuditRows
{
    private const string AuditTableName = "AuditLog";
    private const string PartitionFormat = "yyyyMM";

    public static async Task<IReadOnlyList<TableEntity>> ListAsync(string storageConnectionString, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageConnectionString);

        var table = new TableServiceClient(storageConnectionString).GetTableClient(AuditTableName);
        var now = DateTimeOffset.UtcNow.UtcDateTime;
        var rows = new List<TableEntity>();
        foreach (var partition in new[] { now, now.AddMonths(-1) }.Select(date => date.ToString(PartitionFormat, CultureInfo.InvariantCulture)))
        {
            var filter = TableClient.CreateQueryFilter($"PartitionKey eq {partition}");
            await foreach (var entity in table.QueryAsync<TableEntity>(filter, cancellationToken: cancellationToken))
            {
                rows.Add(entity);
            }
        }

        return rows;
    }
}
