// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using Azure.Data.Tables;

namespace SharpVault.IntegrationTests.Storage;

public static class AuditRows
{
    private const string PartitionFormat = "yyyyMM";

    public static async Task<IReadOnlyList<TableEntity>> ListAsync(TableClient table, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(table);

        var now = DateTimeOffset.UtcNow;
        var partitions = new[]
        {
            now.UtcDateTime.ToString(PartitionFormat, CultureInfo.InvariantCulture),
            now.UtcDateTime.AddMonths(-1).ToString(PartitionFormat, CultureInfo.InvariantCulture),
        };

        var rows = new List<TableEntity>();
        foreach (var partition in partitions)
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
