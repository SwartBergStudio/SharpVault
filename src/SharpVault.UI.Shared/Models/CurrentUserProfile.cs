// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Models;

public sealed record CurrentUserProfile(Guid UserId, string DisplayName, bool IsAdmin);
