// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Diagnostics.CodeAnalysis;

namespace SharpVault.Application.Common.Models;

[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "SecretPermission is the plan-fixed name of the share permission level, not a flags enum or a permission set.")]
public enum SecretPermission
{
    None = 0,
    View = 1,
    Edit = 2,
    Manage = 3,
    Owner = 4,
}
