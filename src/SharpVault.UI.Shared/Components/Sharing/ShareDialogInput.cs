// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Directory;
using SharpVault.Dtos.Sharing;

namespace SharpVault.UI.Shared.Components.Sharing;

public sealed record ShareDialogInput(DirectoryUserDto Recipient, SharePermissionDto Permission);
