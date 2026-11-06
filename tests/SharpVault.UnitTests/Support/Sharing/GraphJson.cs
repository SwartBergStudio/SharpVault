// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;

namespace SharpVault.UnitTests.Support.Sharing;

internal static class GraphJson
{
    public static string Users(params (Guid Id, string? DisplayName, string? Mail, string? UserPrincipalName)[] users)
    {
        var items = users.Select(user => new Dictionary<string, object?>
        {
            ["@odata.id"] = "ignored",
            ["id"] = user.Id,
            ["displayName"] = user.DisplayName,
            ["mail"] = user.Mail,
            ["userPrincipalName"] = user.UserPrincipalName,
        }).ToList();
        return JsonSerializer.Serialize(new { value = items });
    }

    public static string User(Guid id, string? displayName, string? mail, string? userPrincipalName) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["displayName"] = displayName,
            ["mail"] = mail,
            ["userPrincipalName"] = userPrincipalName,
        });
}
