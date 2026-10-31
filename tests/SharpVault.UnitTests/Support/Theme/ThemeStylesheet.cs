// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Theme;

internal static class ThemeStylesheet
{
    public const string RelativePath = "src/SharpVault.UI.Shared/wwwroot/css/app.css";
    public const string LightMediaPrelude = "@media (prefers-color-scheme: light)";

    public static string Path { get; } = UiSourceFiles.Absolute(RelativePath);

    public static string Load() => File.ReadAllText(Path);

    public static IReadOnlyList<(string Name, string Value)> DarkDeclarations()
    {
        var root = CssBlocks.TopLevel(Load()).First(block => block.Prelude == ":root");
        return CssBlocks.Declarations(root.Body);
    }

    public static IReadOnlyList<(string Name, string Value)> LightDeclarations()
    {
        var media = CssBlocks.TopLevel(Load()).Single(block => block.Prelude == LightMediaPrelude);
        var root = CssBlocks.TopLevel(media.Body).Single(block => block.Prelude == ":root");
        return CssBlocks.Declarations(root.Body);
    }

    public static IReadOnlyDictionary<string, string> DarkTokens() => Tokens(DarkDeclarations());

    public static IReadOnlyDictionary<string, string> LightTokens() => Tokens(LightDeclarations());

    public static IReadOnlyDictionary<string, string> Tokens(string scheme) =>
        scheme == "light" ? LightTokens() : DarkTokens();

    private static Dictionary<string, string> Tokens(IReadOnlyList<(string Name, string Value)> declarations) =>
        declarations
            .Where(declaration => declaration.Name.StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(declaration => declaration.Name, declaration => declaration.Value, StringComparer.Ordinal);
}
