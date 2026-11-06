// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Audit;
using SharpVault.Domain.Users;

namespace SharpVault.Application.Features.Users.Access;

public sealed partial class UserAccessGate(
    IUserStore users,
    IAuditLog auditLog,
    IUserAccessCache cache,
    TimeProvider timeProvider,
    IOptions<UserAccessOptions> options,
    ILogger<UserAccessGate> logger) : IUserAccessGate
{
    public async Task<UserAccessDecision> CheckAsync(SignedInUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);

        var now = timeProvider.GetUtcNow();
        if (cache.TryGet(user.UserId, out var cached))
        {
            if (cached.Status != UserStatus.Active)
            {
                return UserAccessDecision.Denied;
            }

            if (!IsSignInDue(cached.LastSignInAt, now)
                && !RoleObservation.IsNewer(cached.IsAdmin, cached.RoleObservedAt, user.IsAdmin, user.TokenIssuedAt))
            {
                return UserAccessDecision.Allowed;
            }
        }

        var stored = await users.GetAsync(user.UserId, cancellationToken);
        if (stored is null)
        {
            return await ProvisionAsync(user, now, cancellationToken);
        }

        if (stored.Value.Status != UserStatus.Active)
        {
            CacheState(stored.Value);
            LogLockedUserDenied(logger, user.UserId);
            return UserAccessDecision.Denied;
        }

        if (IsSignInDue(stored.Value.LastSignInAt, now))
        {
            return await RecordSignInAsync(user, stored, now, cancellationToken);
        }

        if (RoleObservation.IsNewer(stored.Value.IsAdmin, stored.Value.RoleObservedAt, user.IsAdmin, user.TokenIssuedAt))
        {
            return await RecordRoleAsync(user, stored, cancellationToken);
        }

        CacheState(stored.Value);
        return UserAccessDecision.Allowed;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "User {UserId} provisioned")]
    private static partial void LogUserProvisioned(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sign-in recorded for user {UserId}")]
    private static partial void LogSignInRecorded(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Role observation recorded for user {UserId}: admin {IsAdmin}")]
    private static partial void LogRoleRecorded(ILogger logger, Guid userId, bool isAdmin);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Access denied for locked user {UserId}")]
    private static partial void LogLockedUserDenied(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sign-in write for user {UserId} lost to a concurrent change")]
    private static partial void LogSignInWriteConflict(ILogger logger, Guid userId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Sign-in audit entry for user {UserId} could not be written")]
    private static partial void LogSignInAuditFailed(ILogger logger, Guid userId, Exception exception);

    private static UserAccessDecision Decide(UserStatus status) =>
        status == UserStatus.Active ? UserAccessDecision.Allowed : UserAccessDecision.Denied;

    private bool IsSignInDue(DateTimeOffset lastSignInAt, DateTimeOffset now) =>
        now - lastSignInAt >= options.Value.SignInRecordInterval;

    private void CacheState(User user) =>
        cache.Set(user.Id, new CachedUserAccess(user.Status, user.LastSignInAt, user.IsAdmin, user.RoleObservedAt));

    private static (bool IsAdmin, DateTimeOffset? ObservedAt) ObserveRole(bool storedIsAdmin, DateTimeOffset? storedAt, SignedInUser user) =>
        RoleObservation.IsNewer(storedIsAdmin, storedAt, user.IsAdmin, user.TokenIssuedAt)
            ? (user.IsAdmin, user.TokenIssuedAt)
            : (storedIsAdmin, storedAt);

    private async Task<UserAccessDecision> ProvisionAsync(SignedInUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var (isAdmin, observedAt) = ObserveRole(false, null, user);
        var newUser = new User
        {
            Id = user.UserId,
            DisplayName = UserProfileText.NormalizeDisplayName(user.DisplayName),
            Email = UserProfileText.NormalizeEmail(user.Email),
            Status = UserStatus.Active,
            CreatedAt = now,
            LastSignInAt = now,
            IsAdmin = isAdmin,
            RoleObservedAt = observedAt,
        };

        var result = await users.AddAsync(newUser, cancellationToken);
        if (result != StoreWriteResult.Written)
        {
            return await ReloadAsync(user.UserId, cancellationToken);
        }

        await AppendSignInAsync(user, now, cancellationToken);
        CacheState(newUser);
        LogUserProvisioned(logger, user.UserId);
        return UserAccessDecision.Allowed;
    }

    private async Task<UserAccessDecision> RecordSignInAsync(
        SignedInUser user,
        Versioned<User> stored,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (isAdmin, observedAt) = ObserveRole(stored.Value.IsAdmin, stored.Value.RoleObservedAt, user);
        var updated = new User
        {
            Id = stored.Value.Id,
            DisplayName = UserProfileText.NormalizeDisplayName(user.DisplayName),
            Email = UserProfileText.NormalizeEmail(user.Email),
            Status = stored.Value.Status,
            CreatedAt = stored.Value.CreatedAt,
            LastSignInAt = now,
            IsAdmin = isAdmin,
            RoleObservedAt = observedAt,
        };

        var result = await users.ReplaceAsync(updated, stored.ETag, cancellationToken);
        if (result != StoreWriteResult.Written)
        {
            LogSignInWriteConflict(logger, user.UserId);
            return await ReloadAsync(user.UserId, cancellationToken);
        }

        await AppendSignInAsync(user, now, cancellationToken);
        CacheState(updated);
        LogSignInRecorded(logger, user.UserId);
        return UserAccessDecision.Allowed;
    }

    private async Task<UserAccessDecision> RecordRoleAsync(
        SignedInUser user,
        Versioned<User> stored,
        CancellationToken cancellationToken)
    {
        var updated = new User
        {
            Id = stored.Value.Id,
            DisplayName = stored.Value.DisplayName,
            Email = stored.Value.Email,
            Status = stored.Value.Status,
            CreatedAt = stored.Value.CreatedAt,
            LastSignInAt = stored.Value.LastSignInAt,
            IsAdmin = user.IsAdmin,
            RoleObservedAt = user.TokenIssuedAt,
        };

        var result = await users.ReplaceAsync(updated, stored.ETag, cancellationToken);
        if (result != StoreWriteResult.Written)
        {
            LogSignInWriteConflict(logger, user.UserId);
            return await ReloadAsync(user.UserId, cancellationToken);
        }

        CacheState(updated);
        LogRoleRecorded(logger, user.UserId, user.IsAdmin);
        return UserAccessDecision.Allowed;
    }

    private async Task<UserAccessDecision> ReloadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var reloaded = await users.GetAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException(UserAccessMessages.UserRowMissing);

        CacheState(reloaded.Value);
        return Decide(reloaded.Value.Status);
    }

    private async Task AppendSignInAsync(SignedInUser user, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var entry = new AuditEntry
        {
            Id = Guid.NewGuid(),
            EventType = AuditEventType.SignIn,
            ActorId = user.UserId,
            TargetId = null,
            OccurredAt = now,
            IpAddress = UserProfileText.NormalizeIpAddress(user.IpAddress),
            Reason = null,
        };

        try
        {
            await auditLog.AppendAsync(entry, cancellationToken);
        }
        catch (Exception exception)
        {
            LogSignInAuditFailed(logger, user.UserId, exception);
            throw;
        }
    }
}
