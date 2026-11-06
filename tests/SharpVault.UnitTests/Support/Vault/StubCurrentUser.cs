// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Application.Common.Interfaces;

namespace SharpVault.UnitTests.Support.Vault;

internal sealed class StubCurrentUser(Guid? userId = null, bool isAdmin = false) : ICurrentUser
{
    public Guid UserId { get; } = userId ?? Guid.NewGuid();

    public string DisplayName => "Stub User";

    public bool IsAdmin { get; } = isAdmin;
}
