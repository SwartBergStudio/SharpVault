// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpVault.Infrastructure.Encryption.Development;

namespace SharpVault.Infrastructure.Encryption;

public sealed partial class KeyWrapProviderStartupCheck(
    IKeyWrapProvider keyWrapProvider,
    ILogger<KeyWrapProviderStartupCheck> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (keyWrapProvider is DevelopmentKeyWrapProvider)
        {
            LogDevelopmentProviderInUse(logger);
        }
        else
        {
            LogProviderInUse(logger, keyWrapProvider.GetType().Name);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Information, Message = "Data keys are wrapped by {KeyWrapProvider}")]
    private static partial void LogProviderInUse(ILogger logger, string keyWrapProvider);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Data keys are wrapped by the development key wrap provider, which is only allowed in the Development environment")]
    private static partial void LogDevelopmentProviderInUse(ILogger logger);
}
