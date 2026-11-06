// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public enum VaultItemKind
{
    ActiveSecret = 1,
    RecycledSecret = 2,
    ActiveFolder = 3,
    RecycledFolder = 4,
    Version = 5,
}
