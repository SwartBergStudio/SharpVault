// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;

namespace SharpVault.IntegrationTests.Support;

public sealed class InterleavingUserStore(
    IUserStore inner,
    Guid targetUserId,
    Func<CancellationToken, Task> afterFirstGet) : IUserStore
{
    private int _intercepted;

    public async Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var result = await inner.GetAsync(userId, cancellationToken);
        if (userId == targetUserId && Interlocked.Exchange(ref _intercepted, 1) == 0)
        {
            await afterFirstGet(cancellationToken);
        }

        return result;
    }

    public Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken) =>
        inner.AddAsync(user, cancellationToken);

    public Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken) =>
        inner.ReplaceAsync(user, etag, cancellationToken);

    public Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken) =>
        inner.ListAsync(pageSize, afterUserId, cancellationToken);
}
