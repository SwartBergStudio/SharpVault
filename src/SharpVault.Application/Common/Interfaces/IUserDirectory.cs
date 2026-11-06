// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Common.Interfaces;

public interface IUserDirectory
{
    Task<IReadOnlyList<DirectoryUser>> SearchAsync(string term, int limit, CancellationToken cancellationToken);

    Task<DirectoryUser?> FindMemberAsync(Guid userId, CancellationToken cancellationToken);
}
