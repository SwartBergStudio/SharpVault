// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure.Data.Tables;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpVault.Infrastructure.Storage;

public sealed partial class TableStorageInitializer(
    TableServiceClient tableServiceClient,
    TableStorageTableSet tableSet,
    ILogger<TableStorageInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (tableSet.Names.Count > 0)
        {
            var existing = await ListExistingTablesAsync(cancellationToken);
            foreach (var name in tableSet.Names.Where(name => !existing.Contains(name)))
            {
                await tableServiceClient.CreateTableIfNotExistsAsync(name, cancellationToken);
            }
        }

        LogTablesInitialised(logger, tableSet.Names.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task<HashSet<string>> ListExistingTablesAsync(CancellationToken cancellationToken)
    {
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var table in tableServiceClient.QueryAsync(cancellationToken: cancellationToken))
        {
            existing.Add(table.Name);
        }

        return existing;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Table Storage initialised with {TableCount} tables")]
    private static partial void LogTablesInitialised(ILogger logger, int tableCount);
}
