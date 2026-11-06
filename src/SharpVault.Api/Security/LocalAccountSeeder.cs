// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Authentication.Identity;
using SharpVault.Authentication.Local;
using SharpVault.Domain.Users;

namespace SharpVault.Api.Security;

public sealed partial class LocalAccountSeeder(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    ILogger<LocalAccountSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var now = timeProvider.GetUtcNow();

        foreach (var account in LocalAccounts.All)
        {
            var user = new User
            {
                Id = account.Id,
                DisplayName = account.DisplayName,
                Email = account.Email,
                Status = UserStatus.Active,
                CreatedAt = now,
                LastSignInAt = DateTimeOffset.UnixEpoch,
                IsAdmin = account.Roles.Contains(AppRoles.Admin),
                RoleObservedAt = now,
            };

            await SeedAsync(users, user, cancellationToken);
        }
    }

    private async Task SeedAsync(IUserStore users, User user, CancellationToken cancellationToken)
    {
        try
        {
            var result = await users.AddAsync(user, cancellationToken);
            if (result is not (StoreWriteResult.Written or StoreWriteResult.AlreadyExists))
            {
                LogSeedSkipped(logger, result);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogSeedFailed(logger, exception);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "A local account could not be seeded because the user store failed")]
    private static partial void LogSeedFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A local account could not be seeded: {Result}")]
    private static partial void LogSeedSkipped(ILogger logger, StoreWriteResult result);
}
