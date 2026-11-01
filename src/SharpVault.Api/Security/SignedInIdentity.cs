// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.ServiceDefaults.Identity;

namespace SharpVault.Api.Security;

public sealed record SignedInIdentity(Guid UserId, string? DisplayName, string? Email, AccountKind AccountKind, bool IsAdmin);
