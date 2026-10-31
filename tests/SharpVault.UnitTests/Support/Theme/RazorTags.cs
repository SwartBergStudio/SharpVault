// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;

namespace SharpVault.UnitTests.Support.Theme;

internal sealed record RazorTag(string Name, string Attributes, string Text);

internal static partial class RazorTags
{
    public static IEnumerable<RazorTag> Find(string razor) =>
        OpeningTags().Matches(razor).Select(match =>
            new RazorTag(match.Groups["name"].Value, match.Groups["attrs"].Value, match.Value));

    public static bool HasAttribute(RazorTag tag, string attributeName) =>
        Regex.IsMatch(
            tag.Attributes,
            "(?<![\\w:-])" + Regex.Escape(attributeName) + "\\s*=",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

    public static string? AttributeValue(RazorTag tag, string attributeName)
    {
        var match = Regex.Match(
            tag.Attributes,
            "(?<![\\w:-])" + Regex.Escape(attributeName) + "\\s*=\\s*\"(?<value>[^\"]*)\"",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));
        return match.Success ? match.Groups["value"].Value : null;
    }

    [GeneratedRegex(
        "<(?<name>button|a|input|select|textarea|summary|NavLink|InputText|InputTextArea|InputSelect|InputCheckbox|InputNumber|InputDate|InputRadio|InputFile)\\b(?<attrs>(?:[^>\"']|\"[^\"]*\"|'[^']*')*)>",
        RegexOptions.CultureInvariant)]
    private static partial Regex OpeningTags();
}
