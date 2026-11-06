// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.Dtos.Vault;

namespace SharpVault.UI.Shared.Components.Vault;

public static class SecretDisplayName
{
    public const string DamagedName = "Unreadable item";

    public static string For(SecretDto secret)
    {
        ArgumentNullException.ThrowIfNull(secret);

        return secret.IsDamaged || string.IsNullOrWhiteSpace(secret.Name) ? DamagedName : secret.Name;
    }
}
