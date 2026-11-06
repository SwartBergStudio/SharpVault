// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;

namespace SharpVault.UnitTests.Support.Theme;

internal static partial class EventHandlerAttributes
{
    public static IReadOnlyList<string> Find(string markup)
    {
        var offences = new List<string>();
        foreach (Match tag in OpeningTag().Matches(markup))
        {
            var tagName = tag.Groups["name"].Value;
            var attributes = QuotedOrGrouped().Replace(tag.Groups["attrs"].Value, " ");
            foreach (Match attribute in AttributeName().Matches(attributes))
            {
                var name = attribute.Groups["name"].Value;
                if (IsInlineHandler(tagName, name))
                {
                    offences.Add($"<{tagName} {name}=");
                }
            }
        }

        return offences;
    }

    private static bool IsInlineHandler(string tagName, string attributeName)
    {
        if (LowercaseHandler().IsMatch(attributeName))
        {
            return true;
        }

        return char.IsLower(tagName[0]) && AnyCaseHandler().IsMatch(attributeName);
    }

    [GeneratedRegex("""<(?<name>[A-Za-z][\w.:-]*)(?<attrs>(?:[^>"'(]|"[^"]*"|'[^']*'|\((?:[^()]|\([^()]*\))*\))*)>""", RegexOptions.CultureInvariant)]
    private static partial Regex OpeningTag();

    [GeneratedRegex("""
        "[^"]*"|'[^']*'|\((?:[^()]|\([^()]*\))*\)
        """, RegexOptions.CultureInvariant)]
    private static partial Regex QuotedOrGrouped();

    [GeneratedRegex("""(?<=\s)(?<name>[A-Za-z][\w:.-]*)\s*=""", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeName();

    [GeneratedRegex("^on[a-z]+$", RegexOptions.CultureInvariant)]
    private static partial Regex LowercaseHandler();

    [GeneratedRegex("^on[a-z]+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AnyCaseHandler();
}
