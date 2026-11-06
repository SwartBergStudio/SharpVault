// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Domain.Vaults;

namespace SharpVault.Application.Common.Models;

public sealed record StagedFolder(Folder Folder, string SourceETag);
