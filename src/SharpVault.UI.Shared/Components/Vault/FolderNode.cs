// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public sealed record FolderNode(FolderDto Folder, string DisplayName, IReadOnlyList<FolderNode> Children);
