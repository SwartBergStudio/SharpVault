// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public enum ResultFailureKind
{
    Invalid = 0,
    NotFound = 1,
    Conflict = 2,
    Rejected = 3,
    Forbidden = 4,
}
