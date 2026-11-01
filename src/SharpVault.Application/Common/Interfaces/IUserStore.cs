// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;

namespace SharpVault.Application.Common.Interfaces;

public interface IUserStore
{
    Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken);

    Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken);

    Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken);
}
