// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using Azure;
using SharpVault.Application.Common.Interfaces;
using SharpVault.Application.Common.Models;
using SharpVault.Domain.Users;

namespace SharpVault.IntegrationTests.Support;

public sealed class ThrowingUserStore : IUserStore
{
    private const int ServiceUnavailable = 503;
    private const string Message = "Service unavailable";

    public Task<Versioned<User>?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        throw new RequestFailedException(ServiceUnavailable, Message);

    public Task<StoreWriteResult> AddAsync(User user, CancellationToken cancellationToken) =>
        throw new RequestFailedException(ServiceUnavailable, Message);

    public Task<StoreWriteResult> ReplaceAsync(User user, string etag, CancellationToken cancellationToken) =>
        throw new RequestFailedException(ServiceUnavailable, Message);

    public Task<UserListPage> ListAsync(int pageSize, Guid? afterUserId, CancellationToken cancellationToken) =>
        throw new RequestFailedException(ServiceUnavailable, Message);
}
