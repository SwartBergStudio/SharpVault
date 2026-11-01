// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Globalization;
using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed partial class ThemeContrastTests
{
    private const double TextMinimum = 4.5;
    private const double BoundaryMinimum = 3.0;
    private const string Dark = "dark";
    private const string Light = "light";

    private static readonly string[] Schemes = [Dark, Light];
    private static readonly string[] TextBackgrounds = ["bg", "surface", "surface-raised", "surface-sunken", "accent-soft"];
    private static readonly string[] StatusNames = ["success", "locked", "warning", "danger"];
    private static readonly string[] StatusExtraBackgrounds = ["surface", "surface-raised", "bg"];
    private static readonly string[] ControlBackgrounds = ["bg", "surface", "surface-raised", "surface-sunken"];

    private static readonly string[] RequiredTokens =
    [
        "--color-bg", "--color-bg-glow", "--color-surface", "--color-surface-raised", "--color-surface-sunken",
        "--color-border", "--color-border-strong", "--color-rivet", "--color-text", "--color-text-muted",
        "--color-accent", "--color-accent-hover", "--color-on-accent", "--color-accent-soft", "--color-focus",
        "--color-success", "--color-success-bg", "--color-success-border",
        "--color-locked", "--color-locked-bg", "--color-locked-border",
        "--color-warning", "--color-warning-bg", "--color-warning-border",
        "--color-danger", "--color-danger-bg", "--color-danger-border",
        "--color-danger-solid", "--color-danger-solid-hover", "--color-on-danger",
        "--color-grain", "--color-overlay", "--shadow-raised", "--shadow-dialog",
        "--font-sans", "--font-mono",
        "--text-xs", "--text-sm", "--text-md", "--text-lg", "--text-xl", "--text-2xl", "--text-3xl",
        "--leading-tight", "--leading-body",
        "--stretch-body", "--stretch-heading", "--stretch-plate", "--tracking-plate",
        "--space-xs", "--space-sm", "--space-md", "--space-lg", "--space-xl", "--space-2xl",
        "--radius-sm", "--radius-md", "--radius-lg", "--radius-round",
        "--duration-fast", "--duration-normal", "--duration-seal", "--ease-out", "--ease-lock",
        "--header-height", "--sidenav-width", "--content-max", "--control-height", "--tap-target",
        "--z-header", "--z-dialog", "--z-lock", "--z-error-ui", "--z-skip",
    ];

    public static TheoryData<string, string, string, double> TokenPairs()
    {
        var data = new TheoryData<string, string, string, double>();
        foreach (var scheme in Schemes)
        {
            foreach (var pair in PairsFor())
            {
                data.Add(scheme, pair.Foreground, pair.Background, pair.Minimum);
            }
        }

        return data;
    }

    [Fact]
    public void ThemeContrast_PlannedPairs_CountFiftyPerScheme()
    {
        Assert.Equal(50, PairsFor().Count());
        Assert.Equal(100, TokenPairs().Count);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(TokenPairs))]
    public void ThemeContrast_TokenPair_MeetsWcagAa(string scheme, string foreground, string background, double minimum)
    {
        var tokens = ThemeStylesheet.Tokens(scheme);
        var foregroundHex = tokens[foreground];
        var backgroundHex = tokens[background];

        var ratio = WcagContrast.Ratio(foregroundHex, backgroundHex);

        Assert.True(
            ratio >= minimum,
            string.Create(
                CultureInfo.InvariantCulture,
                $"{scheme}: {foreground} {foregroundHex} on {background} {backgroundHex} is {ratio:0.00}:1, minimum {minimum:0.0}:1."));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ThemeTokens_LightScheme_DefinesEveryDarkColourToken()
    {
        var dark = ThemeStylesheet.DarkTokens().Keys.Where(IsSchemeToken).Order(StringComparer.Ordinal).ToList();
        var lightDeclarations = ThemeStylesheet.LightDeclarations();
        var light = ThemeStylesheet.LightTokens().Keys.Where(IsSchemeToken).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(dark, light);
        Assert.All(
            lightDeclarations,
            declaration => Assert.True(
                declaration.Name == "color-scheme" || IsSchemeToken(declaration.Name),
                $"The light block declares '{declaration.Name}'."));
    }

    [Fact]
    public void ThemeTokens_ColourSchemeProperty_IsDeclaredPerScheme()
    {
        var dark = ThemeStylesheet.DarkDeclarations().Single(declaration => declaration.Name == "color-scheme");
        var light = ThemeStylesheet.LightDeclarations().Single(declaration => declaration.Name == "color-scheme");

        Assert.Equal("dark", dark.Value);
        Assert.Equal("light", light.Value);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ThemeTokens_ColourTokens_AreOpaqueHex()
    {
        foreach (var scheme in Schemes)
        {
            var colours = ThemeStylesheet.Tokens(scheme).Where(token => token.Key.StartsWith("--color-", StringComparison.Ordinal));
            foreach (var (name, value) in colours)
            {
                var translucent = name.EndsWith("-grain", StringComparison.Ordinal)
                    || name.EndsWith("-overlay", StringComparison.Ordinal);
                var pattern = translucent ? TranslucentColour() : OpaqueHex();
                Assert.True(pattern.IsMatch(value), $"{scheme}: {name} has value '{value}'.");
            }
        }
    }

    [Fact]
    public void ThemeTokens_RequiredTokens_AreDefined()
    {
        var tokens = ThemeStylesheet.DarkTokens();

        var missing = RequiredTokens.Where(name => !tokens.ContainsKey(name)).ToList();

        Assert.True(missing.Count == 0, "Missing tokens: " + string.Join(", ", missing));
    }

    [Fact]
    public void ThemeTokens_RemovedTokens_AreNotDefined()
    {
        var tokens = ThemeStylesheet.DarkTokens();

        Assert.DoesNotContain("--color-error-bg", tokens.Keys);
        Assert.DoesNotContain("--color-error-text", tokens.Keys);
        Assert.DoesNotContain("--color-shadow", tokens.Keys);
    }

    [Fact]
    public void ThemeTokens_FirstRootRule_HoldsEveryToken()
    {
        var css = ThemeStylesheet.Load();
        var roots = CssBlocks.TopLevel(css).Where(block => block.Prelude == ":root").ToList();

        Assert.Single(roots);
        var referenced = TokenReference().Matches(css).Select(match => match.Groups["name"].Value).Distinct().ToList();
        var undefined = referenced.Where(name => !ThemeStylesheet.DarkTokens().ContainsKey(name)).ToList();
        Assert.True(undefined.Count == 0, "Undefined tokens used: " + string.Join(", ", undefined));
    }

    [Theory]
    [InlineData("#000000", "#FFFFFF", 21.00)]
    [InlineData("#777777", "#FFFFFF", 4.48)]
    [InlineData("#FFFFFF", "#FFFFFF", 1.00)]
    [InlineData("#0A1017", "#0A1017", 1.00)]
    [InlineData("#FFFFFF", "#000000", 21.00)]
    [InlineData("#FFFFFF", "#777777", 4.48)]
    public void WcagContrast_KnownPairs_MatchReferenceValues(string first, string second, double expected)
    {
        var ratio = WcagContrast.Ratio(first, second);

        Assert.Equal(expected, ratio, 2);
    }

    [Fact]
    public void WcagContrast_ArgumentOrder_DoesNotMatter()
    {
        Assert.Equal(WcagContrast.Ratio("#D9AE55", "#0A1017"), WcagContrast.Ratio("#0A1017", "#D9AE55"));
    }

    [Theory]
    [InlineData("000000")]
    [InlineData("#FFF")]
    [InlineData("rgba(0, 0, 0, 0.5)")]
    public void WcagContrast_NonHexColour_Throws(string value)
    {
        Assert.Throws<FormatException>(() => WcagContrast.Ratio(value, "#FFFFFF"));
    }

    private static IEnumerable<(string Foreground, string Background, double Minimum)> PairsFor()
    {
        foreach (var foreground in new[] { "text", "text-muted", "accent" })
        {
            foreach (var background in TextBackgrounds)
            {
                yield return (Token(foreground), Token(background), TextMinimum);
            }
        }

        yield return (Token("on-accent"), Token("accent"), TextMinimum);
        yield return (Token("on-accent"), Token("accent-hover"), TextMinimum);
        yield return (Token("on-danger"), Token("danger-solid"), TextMinimum);
        yield return (Token("on-danger"), Token("danger-solid-hover"), TextMinimum);

        foreach (var status in StatusNames)
        {
            yield return (Token(status), Token(status + "-bg"), TextMinimum);
            foreach (var background in StatusExtraBackgrounds)
            {
                yield return (Token(status), Token(background), TextMinimum);
            }

            yield return (Token("text"), Token(status + "-bg"), TextMinimum);
        }

        foreach (var background in ControlBackgrounds)
        {
            yield return (Token("border-strong"), Token(background), BoundaryMinimum);
            yield return (Token("focus"), Token(background), BoundaryMinimum);
        }

        yield return (Token("accent"), Token("surface"), BoundaryMinimum);
        yield return (Token("danger-solid"), Token("surface"), BoundaryMinimum);
        yield return (Token("danger-solid"), Token("surface-raised"), BoundaryMinimum);
    }

    private static string Token(string name) => "--color-" + name;

    private static bool IsSchemeToken(string name) =>
        name.StartsWith("--color-", StringComparison.Ordinal) || name.StartsWith("--shadow-", StringComparison.Ordinal);

    [GeneratedRegex("^#[0-9A-F]{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueHex();

    [GeneratedRegex("^rgba\\(", RegexOptions.CultureInvariant)]
    private static partial Regex TranslucentColour();

    [GeneratedRegex("var\\((?<name>--[a-z0-9-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex TokenReference();
}
