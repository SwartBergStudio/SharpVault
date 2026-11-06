// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.Infrastructure.Vaults;

internal static class BinaryLengths
{
    public static int Of(params byte[]?[] values)
    {
        var total = 0;
        foreach (var value in values)
        {
            total += value?.Length ?? 0;
        }

        return total;
    }
}
