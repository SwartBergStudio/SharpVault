// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;

namespace SharpVault.UI.Shared.Components;

public static partial class ComponentIds
{
    public static string EnsureValid(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !ValidId().IsMatch(value))
        {
            throw new ArgumentException($"{parameterName} must be lowercase kebab-case.", parameterName);
        }

        return value;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();
}
