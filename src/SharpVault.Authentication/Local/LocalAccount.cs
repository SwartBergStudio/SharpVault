// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Authentication.Local;

public sealed record LocalAccount(Guid Id, string DisplayName, string Email, IReadOnlyList<string> Roles);
