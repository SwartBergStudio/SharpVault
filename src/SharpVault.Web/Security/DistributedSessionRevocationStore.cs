// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Microsoft.Extensions.Caching.Distributed;

namespace SharpVault.Web.Security;

public sealed class DistributedSessionRevocationStore(IDistributedCache cache) : ISessionRevocationStore
{
    private const string KeyPrefix = "sv:session-revoked:";
    private static readonly byte[] Marker = [1];

    public Task RevokeAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var options = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = SessionLifetime.Value };
        return cache.SetAsync(KeyPrefix + sessionId, Marker, options, cancellationToken);
    }

    public async Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        return await cache.GetAsync(KeyPrefix + sessionId, cancellationToken) is not null;
    }
}
