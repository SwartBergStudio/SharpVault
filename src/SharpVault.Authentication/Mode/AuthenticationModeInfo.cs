// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Authentication.Identity;

namespace SharpVault.Authentication.Mode;

public sealed record AuthenticationModeInfo(AuthenticationMode Mode, EntraApplicationSettings Entra);
