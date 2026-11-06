// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.Api.Endpoints.Vault;

public static class VaultQueryParser
{
    public static FolderDeleteModeDto ParseMode(string value)
    {
        foreach (var name in Enum.GetNames<FolderDeleteModeDto>())
        {
            if (string.Equals(name, value, StringComparison.Ordinal))
            {
                return Enum.Parse<FolderDeleteModeDto>(name);
            }
        }

        return default;
    }
}
