// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;
using SharpVault.Application.Common.Models;

namespace SharpVault.Application.Common.Interfaces;

public interface IUserAccessCache
{
    bool TryGet(Guid userId, [NotNullWhen(true)] out CachedUserAccess? access);

    [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Set is the cache write verb and the contract name fixed by the plan.")]
    void Set(Guid userId, CachedUserAccess access);

    /// <summary>
    /// Removes the cached access for a user. <c>feature/user-lock</c> calls this after writing a lock.
    /// </summary>
    void Invalidate(Guid userId);
}
