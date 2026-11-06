// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Application.Common.Models;

public static class ResultMessages
{
    public const string NotFound = "The item was not found.";

    public const string Forbidden = "You cannot perform this action.";

    public const string Conflict = "The item was changed by another request. Reload and try again.";
}
