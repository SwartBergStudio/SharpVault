// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Users;

public static class UserKeys
{
    public const string PartitionKey = "user";

    public static string RowKey(Guid userId) => userId.ToString("D");
}
