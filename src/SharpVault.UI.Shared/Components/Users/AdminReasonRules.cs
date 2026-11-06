// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UI.Shared.Components.Users;

public static class AdminReasonRules
{
    public const int MinLength = 20;

    public const int MaxLength = 1000;

    public static bool IsValid(string? reason)
    {
        if (reason is null)
        {
            return false;
        }

        var length = reason.Trim().Length;
        return length is >= MinLength and <= MaxLength && HasWellFormedText(reason);
    }

    private static bool HasWellFormedText(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsHighSurrogate(character) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }

            if (char.IsSurrogate(character) || (char.IsControl(character) && !IsAllowedControl(character)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAllowedControl(char character) => character is '\r' or '\n' or '\t';
}
