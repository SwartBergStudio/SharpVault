// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text;
using System.Text.RegularExpressions;

namespace SharpVault.UnitTests.Support.Theme;

internal sealed record CssBlock(IReadOnlyList<string> Context, string Prelude, string Body)
{
    public IReadOnlyList<string> Selectors => CssBlocks.SplitSelectorList(Prelude);

    public IReadOnlyList<(string Name, string Value)> Declarations => CssBlocks.Declarations(Body);

    public bool HasDeclaration(string name, string valueFragment) =>
        Declarations.Any(declaration =>
            declaration.Name == name && declaration.Value.Contains(valueFragment, StringComparison.Ordinal));

    public string? ValueOf(string name) =>
        Declarations.LastOrDefault(declaration => declaration.Name == name).Value;
}

internal static partial class CssBlocks
{
    public static IReadOnlyList<(string Prelude, string Body)> TopLevel(string css)
    {
        var blocks = new List<(string, string)>();
        var depth = 0;
        var preludeStart = 0;
        var bodyStart = 0;
        var prelude = string.Empty;

        for (var index = 0; index < css.Length; index++)
        {
            switch (css[index])
            {
                case '{':
                    if (depth == 0)
                    {
                        prelude = NormaliseWhitespace(css[preludeStart..index]);
                        bodyStart = index + 1;
                    }

                    depth++;
                    break;
                case '}':
                    depth--;
                    if (depth == 0)
                    {
                        blocks.Add((prelude, css[bodyStart..index]));
                        preludeStart = index + 1;
                    }

                    break;
                case ';' when depth == 0:
                    preludeStart = index + 1;
                    break;
            }
        }

        return blocks;
    }

    public static IReadOnlyList<CssBlock> Flatten(string css)
    {
        var result = new List<CssBlock>();
        Collect(css, [], result);
        return result;
    }

    public static IReadOnlyList<(string Name, string Value)> Declarations(string body)
    {
        var declarations = new List<(string, string)>();
        var current = new StringBuilder();
        var depth = 0;

        foreach (var character in body)
        {
            if (character == '{')
            {
                if (depth == 0)
                {
                    current.Clear();
                }

                depth++;
                continue;
            }

            if (character == '}')
            {
                depth--;
                continue;
            }

            if (depth > 0)
            {
                continue;
            }

            if (character == ';')
            {
                AddDeclaration(declarations, current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        AddDeclaration(declarations, current.ToString());
        return declarations;
    }

    public static IReadOnlyList<string> SplitSelectorList(string prelude)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var nesting = 0;

        foreach (var character in prelude)
        {
            if (character is '(' or '[')
            {
                nesting++;
            }
            else if (character is ')' or ']')
            {
                nesting--;
            }

            if (character == ',' && nesting == 0)
            {
                parts.Add(NormaliseWhitespace(current.ToString()));
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        parts.Add(NormaliseWhitespace(current.ToString()));
        return parts;
    }

    public static string NormaliseWhitespace(string value) =>
        Whitespace().Replace(value, " ").Trim();

    private static void Collect(string css, IReadOnlyList<string> context, List<CssBlock> result)
    {
        foreach (var (prelude, body) in TopLevel(css))
        {
            result.Add(new CssBlock(context, prelude, body));
            if (body.Contains('{', StringComparison.Ordinal))
            {
                Collect(body, [.. context, prelude], result);
            }
        }
    }

    private static void AddDeclaration(List<(string, string)> declarations, string statement)
    {
        var separator = statement.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return;
        }

        var name = statement[..separator].Trim();
        var value = NormaliseWhitespace(statement[(separator + 1)..]);
        declarations.Add((name, value));
    }

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
