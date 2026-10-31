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
        foreach (var name in tableSet.Names)
        {
            await tableServiceClient.CreateTableIfNotExistsAsync(name, cancellationToken);
        }

        LogTablesInitialised(logger, tableSet.Names.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Table Storage initialised with {TableCount} tables")]
    private static partial void LogTablesInitialised(ILogger logger, int tableCount);
}
