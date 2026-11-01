// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Interfaces;

/// <summary>
/// The signed-in user of the current request. <see cref="UserId" /> comes from the Entra <c>oid</c> claim.
/// </summary>
public interface ICurrentUser
{
    Guid UserId { get; }

    string DisplayName { get; }

    bool IsAdmin { get; }
}
