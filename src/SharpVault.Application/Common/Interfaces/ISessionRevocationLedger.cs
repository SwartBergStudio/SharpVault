// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Interfaces;

public interface ISessionRevocationLedger
{
    Task RevokeAsync(Guid userId, string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<bool> IsRevokedAsync(Guid userId, string sessionId, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}
