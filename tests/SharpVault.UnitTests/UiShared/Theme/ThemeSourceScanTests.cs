// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed partial class ThemeSourceScanTests
{
    private const string AppCss = "app.css";
    private const string ReconnectModalCss = "ReconnectModal.razor.css";
    private const string InfiniteKeyword = "infinite";

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_UiProjects_ContainNoInlineStyleScriptOrHandlers()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            AddMatches(offences, file, InlineStyleAttribute());
            AddMatches(offences, file, StyleElement());
            AddMatches(offences, file, InlineScriptElement());
            AddMatches(offences, file, EventHandlerAttribute());
            AddMatches(offences, file, JavascriptUrl());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_UiProjects_DoNotUseMarkupString()
    {
        var offences = UiSourceFiles.WithExtension(".razor")
            .Concat(UiSourceFiles.WithExtension(".cs"))
            .Where(file => file.Text.Contains("MarkupString", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(offences.Count == 0, "MarkupString found in: " + string.Join(", ", offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_UiProjects_ReferenceNoExternalOrigins()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            AddMatches(offences, file, ExternalOriginAttribute());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_UiProjects_DeclareNoResourceHints()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            AddMatches(offences, file, ResourceHint());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void RazorSources_InteractiveElements_HaveIds()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            foreach (var tag in RazorTags.Find(file.Text).Where(tag => !RazorTags.HasAttribute(tag, "id")))
            {
                offences.Add($"{file.RelativePath}: {tag.Text}");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_SecretInputs_DisableSpellcheckAutocorrectAndAutocapitalize()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            foreach (var tag in RazorTags.Find(file.Text).Where(IsSecretInput))
            {
                var complete = RazorTags.AttributeValue(tag, "spellcheck") == "false"
                    && RazorTags.AttributeValue(tag, "autocapitalize") == "off"
                    && RazorTags.AttributeValue(tag, "autocorrect") == "off"
                    && RazorTags.HasAttribute(tag, "autocomplete");
                if (!complete)
                {
                    offences.Add($"{file.RelativePath}: {tag.Text}");
                }
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("<input type=\"password\" id=\"x\" />", false)]
    [InlineData("<input type=\"password\" id=\"x\" spellcheck=\"false\" autocapitalize=\"off\" autocorrect=\"off\" autocomplete=\"new-password\" />", true)]
    [InlineData("<InputText class=\"sv-input--secret\" id=\"x\" spellcheck=\"false\" autocapitalize=\"off\" autocomplete=\"off\" />", false)]
    [InlineData("<textarea class=\"a sv-input--secret\" id=\"x\" spellcheck=\"true\" autocapitalize=\"off\" autocorrect=\"off\" autocomplete=\"off\"></textarea>", false)]
    public void SecretInputRule_SampleMarkup_IsRecognisedAsCompleteOrIncomplete(string markup, bool expectedComplete)
    {
        var tag = Assert.Single(RazorTags.Find(markup));

        Assert.True(IsSecretInput(tag));
        var complete = RazorTags.AttributeValue(tag, "spellcheck") == "false"
            && RazorTags.AttributeValue(tag, "autocapitalize") == "off"
            && RazorTags.AttributeValue(tag, "autocorrect") == "off"
            && RazorTags.HasAttribute(tag, "autocomplete");
        Assert.Equal(expectedComplete, complete);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_IconOnlyButtons_HaveAccessibleName()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            foreach (var tag in RazorTags.Find(file.Text).Where(HasIconButtonClass))
            {
                if (!RazorTags.HasAttribute(tag, "aria-label"))
                {
                    offences.Add($"{file.RelativePath}: {tag.Text}");
                }
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_ReferenceOnlyBundledFonts()
    {
        var allowed = PinnedThemeAssets.Woff2Files
            .Select(asset => $"\"../fonts/{asset.FileName}\"")
            .ToHashSet(StringComparer.Ordinal);
        var offences = new List<string>();

        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            var isAppCss = Path.GetFileName(file.RelativePath) == AppCss;
            foreach (Match match in CssUrl().Matches(file.Text))
            {
                var value = match.Groups["value"].Value.Trim();
                if (!isAppCss || !allowed.Contains(value))
                {
                    offences.Add($"{file.RelativePath}: {match.Value}");
                }
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_ContainNoExternalOriginsOrDataUris()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            AddMatches(offences, file, CssExternalReference());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_UseNoImport()
    {
        var offences = UiSourceFiles.WithExtension(".css")
            .Where(file => file.Text.Contains("@import", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(offences.Count == 0, "@import found in: " + string.Join(", ", offences));
    }

    [Fact]
    public void StyleSheets_UiProjects_ContainNoComments()
    {
        var offences = UiSourceFiles.WithExtension(".css")
            .Where(file => file.Text.Contains("/*", StringComparison.Ordinal)
                || file.Text.Contains("*/", StringComparison.Ordinal))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(offences.Count == 0, "CSS comment found in: " + string.Join(", ", offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_UseNoValueAttributeSelectors()
    {
        var offences = UiSourceFiles.WithExtension(".css")
            .Where(file => file.Text.Contains("[value", StringComparison.OrdinalIgnoreCase))
            .Select(file => file.RelativePath)
            .ToList();

        Assert.True(offences.Count == 0, "[value selector found in: " + string.Join(", ", offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_NeverRenderOrMaskValuesWithCss()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            AddMatches(offences, file, TextSecurity());
            AddMatches(offences, file, ContentFromAttribute());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_UseNoLegacyScriptingFeatures()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            AddMatches(offences, file, LegacyScripting());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_InfiniteAnimations_OnlyInBusyIndicatorAndReconnect()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            var name = Path.GetFileName(file.RelativePath);
            if (name == ReconnectModalCss)
            {
                continue;
            }

            var occurrences = Regex.Count(file.Text, InfiniteKeyword, RegexOptions.CultureInvariant);
            if (name != AppCss && occurrences > 0)
            {
                offences.Add($"{file.RelativePath}: {occurrences} occurrence(s)");
            }
        }

        var appCss = ThemeStylesheet.Load();
        var infiniteBlocks = CssBlocks.Flatten(appCss)
            .Where(block => block.Declarations.Any(declaration =>
                declaration.Value.Contains(InfiniteKeyword, StringComparison.Ordinal)))
            .ToList();

        Assert.Equal(1, Regex.Count(appCss, InfiniteKeyword, RegexOptions.CultureInvariant));
        var busyBlock = Assert.Single(infiniteBlocks);
        Assert.Contains(".sv-busy__pointer", busyBlock.Selectors);
        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_App_HonoursReducedMotion()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());
        var rule = blocks.SingleOrDefault(block =>
            block.Context.SequenceEqual(["@layer sv-reset", "@media (prefers-reduced-motion: reduce)"])
            && block.Prelude == "*, *::before, *::after");

        Assert.NotNull(rule);
        foreach (var property in new[] { "animation-duration", "animation-iteration-count", "transition-duration" })
        {
            var declaration = rule.Declarations.SingleOrDefault(candidate => candidate.Name == property);
            Assert.True(
                declaration.Value is not null && declaration.Value.EndsWith("!important", StringComparison.Ordinal),
                $"{property} must be set with !important in the reduced-motion rule.");
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_MonospaceText_DisablesLigatures()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());
        var selectors = new[] { ".sv-mono", ".sv-secret__value", ".sv-input--secret", "code" };

        foreach (var selector in selectors)
        {
            var disabled = blocks.Any(block =>
                block.Selectors.Contains(selector)
                && block.HasDeclaration("font-variant-ligatures", "none")
                && block.HasDeclaration("font-feature-settings", "\"calt\" 0"));
            Assert.True(disabled, $"No rule for '{selector}' disables ligatures.");
        }

        var codeRule = blocks.First(block => block.Selectors.Contains("code"));
        Assert.Equal(["code", "kbd", "samp", "pre"], codeRule.Selectors);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_Print_HidesRevealedValues()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());

        var hidden = blocks.Any(block =>
            block.Context.Contains("@media print")
            && block.Selectors.Contains(".sv-secret[data-state=\"revealed\"] .sv-secret__value")
            && block.HasDeclaration("visibility", "hidden"));

        Assert.True(hidden);
    }

    [Fact]
    public void StyleSheets_ValidationMessages_HaveTextPrefix()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());

        var prefixed = blocks.Any(block =>
            block.Selectors.Contains(".validation-message::before")
            && block.HasDeclaration("content", "\"Error: \""));

        Assert.True(prefixed);
    }

    [Fact]
    public void StyleSheets_CurrentNavigation_HasNonColourIndicator()
    {
        const string active = ".sv-nav__link.active";
        const string current = ".sv-nav__link[aria-current=\"page\"]";
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load())
            .Where(block => block.Selectors.Contains(active)
                && block.Selectors.Contains(current)
                && !block.Context.Contains("@media (forced-colors: active)"))
            .ToList();

        Assert.NotEmpty(blocks);
        var indicator = blocks.Any(block => block.HasDeclaration("box-shadow", "inset"));
        var weights = blocks.Select(block => block.ValueOf("font-weight")).Where(value => value is not null).ToList();
        Assert.True(indicator, "The current navigation item needs an inset bar.");
        Assert.NotEmpty(weights);
        Assert.All(weights, weight => Assert.True(int.Parse(weight!, System.Globalization.CultureInfo.InvariantCulture) >= 650, $"font-weight {weight} is below 650."));
    }

    [Fact]
    public void StyleSheets_FocusVisible_UsesFocusTokenWithOffset()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());

        var rule = blocks.FirstOrDefault(block =>
            block.Selectors.Contains(":focus-visible")
            && block.HasDeclaration("outline", "2px solid var(--color-focus)")
            && block.HasDeclaration("outline-offset", "2px"));

        Assert.NotNull(rule);
    }

    [Fact]
    public void StyleSheets_ForcedColours_IsSupported()
    {
        var blocks = CssBlocks.Flatten(ThemeStylesheet.Load());

        var forced = Assert.Single(blocks, block => block.Prelude == "@media (forced-colors: active)");
        Assert.Contains("CanvasText", forced.Body, StringComparison.Ordinal);
        Assert.Contains("Highlight", forced.Body, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DialogStyles_Backdrop_CoversViewportAndBlocksPointer()
    {
        var css = ThemeStylesheet.Load();
        var backdrop = CssBlocks.Flatten(css).First(block => block.Selectors.Contains(".sv-dialog-backdrop"));

        Assert.True(backdrop.HasDeclaration("position", "fixed"));
        Assert.True(backdrop.HasDeclaration("inset", "0"));
        Assert.DoesNotContain("pointer-events: none", css, StringComparison.Ordinal);
        Assert.DoesNotContain("pointer-events:none", css, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void LockScreenStyles_Background_IsOpaque()
    {
        var lockScreen = CssBlocks.Flatten(ThemeStylesheet.Load())
            .First(block => block.Selectors.Contains(".sv-lockscreen"));

        Assert.True(lockScreen.HasDeclaration("position", "fixed"));
        Assert.True(lockScreen.HasDeclaration("inset", "0"));
        Assert.Equal("var(--color-bg)", lockScreen.ValueOf("background"));
        Assert.DoesNotContain("--color-overlay", lockScreen.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void StyleSheets_FontFaces_UseSwapAndBundledFiles()
    {
        var fontFaces = CssBlocks.TopLevel(ThemeStylesheet.Load())
            .Where(block => block.Prelude == "@font-face")
            .ToList();
        var pinned = PinnedThemeAssets.Woff2Files.Select(asset => asset.FileName).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(4, fontFaces.Count);
        foreach (var (_, body) in fontFaces)
        {
            Assert.Contains("font-display: swap", body, StringComparison.Ordinal);
            Assert.Contains("font-style: normal", body, StringComparison.Ordinal);
            Assert.DoesNotContain("local(", body, StringComparison.Ordinal);
            var url = Assert.Single(CssUrl().Matches(body));
            var fileName = Path.GetFileName(url.Groups["value"].Value.Trim('"'));
            Assert.Contains(fileName, pinned);
        }

        var sources = fontFaces.Select(block => CssUrl().Match(block.Body).Groups["value"].Value).Distinct().Count();
        Assert.Equal(4, sources);
    }

    [Fact]
    public void StyleSheets_App_FollowsPlannedTopLevelOrder()
    {
        var css = ThemeStylesheet.Load();
        var preludes = CssBlocks.TopLevel(css).Select(block => block.Prelude).ToList();

        Assert.StartsWith("@layer sv-reset, sv-base, sv-components, sv-layout, sv-utilities;", css, StringComparison.Ordinal);
        Assert.Equal(
            [
                "@font-face",
                "@font-face",
                "@font-face",
                "@font-face",
                ":root",
                "@media (prefers-color-scheme: light)",
                "@layer sv-reset",
                "@layer sv-base",
                "@keyframes sv-seal-wheel",
                "@keyframes sv-seal-bolts",
                "@keyframes sv-dial-tick",
                "@keyframes sv-reveal",
                "@layer sv-components",
                "@layer sv-layout",
                "@layer sv-utilities",
            ],
            preludes);
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(PinnedFiles))]
    public void FontFiles_Bundled_MatchPinnedSizeAndSha256(string fileName, long size, string sha256)
    {
        var path = UiSourceFiles.Absolute(PinnedThemeAssets.FontDirectory + "/" + fileName);

        Assert.True(File.Exists(path), $"{fileName} is missing.");
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(size, bytes.LongLength);
        Assert.Equal(sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
    }

    [Theory]
    [Trait("Category", "Security")]
    [MemberData(nameof(PinnedWoff2Files))]
    public void FontFiles_Bundled_HaveWoff2Signature(string fileName)
    {
        var path = UiSourceFiles.Absolute(PinnedThemeAssets.FontDirectory + "/" + fileName);

        var header = new byte[4];
        using (var stream = File.OpenRead(path))
        {
            stream.ReadExactly(header);
        }

        Assert.Equal("wOF2", System.Text.Encoding.ASCII.GetString(header));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void FontDirectory_Contents_AreExactlyThePinnedFiles()
    {
        var directory = UiSourceFiles.Absolute(PinnedThemeAssets.FontDirectory);

        var actual = Directory.EnumerateFileSystemEntries(directory)
            .Select(path => Path.GetFileName(path)!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var expected = PinnedThemeAssets.All.Select(asset => asset.FileName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(expected, actual);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UiSharedWebRoot_Files_HaveOnlyExpectedTypes()
    {
        var allowed = new[] { ".css", ".woff2", ".txt", ".svg" };

        var unexpected = UiSourceFiles.AllUiSharedWebRootFiles()
            .Where(file => !allowed.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            .ToList();

        Assert.True(unexpected.Count == 0, "Unexpected web root files: " + string.Join(", ", unexpected));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Favicon_File_ContainsNoScriptStyleOrLinks()
    {
        var path = UiSourceFiles.Absolute(PinnedThemeAssets.FaviconPath);
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.True(bytes.Length <= 1024, $"favicon.svg is {bytes.Length} bytes.");
        foreach (var forbidden in new[] { "<script", "<style", "href", "<foreignObject", "javascript:" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(EventHandlerAttribute(), text);
    }

    [Fact]
    public void Favicon_File_HasPlannedContent()
    {
        const string expected = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 32 32\"><circle cx=\"16\" cy=\"16\" r=\"15\" fill=\"#0A1017\"/><circle cx=\"16\" cy=\"16\" r=\"12\" fill=\"none\" stroke=\"#D9AE55\" stroke-width=\"2\"/><g stroke=\"#D9AE55\" stroke-width=\"2.5\" stroke-linecap=\"round\"><line x1=\"16\" y1=\"16\" x2=\"16\" y2=\"8.5\"/><line x1=\"16\" y1=\"16\" x2=\"22.5\" y2=\"19.75\"/><line x1=\"16\" y1=\"16\" x2=\"9.5\" y2=\"19.75\"/></g><circle cx=\"16\" cy=\"16\" r=\"3\" fill=\"#D9AE55\"/></svg>";

        var text = File.ReadAllText(UiSourceFiles.Absolute(PinnedThemeAssets.FaviconPath));

        Assert.Equal(expected, text.TrimEnd('\n', '\r'));
        Assert.EndsWith("\n", text, StringComparison.Ordinal);
    }

    public static TheoryData<string, long, string> PinnedFiles()
    {
        var data = new TheoryData<string, long, string>();
        foreach (var asset in PinnedThemeAssets.All)
        {
            data.Add(asset.FileName, asset.Size, asset.Sha256);
        }

        return data;
    }

    public static TheoryData<string> PinnedWoff2Files()
    {
        var data = new TheoryData<string>();
        foreach (var asset in PinnedThemeAssets.Woff2Files)
        {
            data.Add(asset.FileName);
        }

        return data;
    }

    private static bool IsSecretInput(RazorTag tag)
    {
        var isInput = tag.Name is "input" or "textarea" || tag.Name.StartsWith("Input", StringComparison.Ordinal);
        if (!isInput)
        {
            return false;
        }

        var type = RazorTags.AttributeValue(tag, "type");
        var cssClass = RazorTags.AttributeValue(tag, "class");
        return string.Equals(type, "password", StringComparison.OrdinalIgnoreCase)
            || (cssClass is not null && cssClass.Contains("sv-input--secret", StringComparison.Ordinal));
    }

    private static bool HasIconButtonClass(RazorTag tag)
    {
        var cssClass = RazorTags.AttributeValue(tag, "class");
        return cssClass is not null && cssClass.Contains("sv-btn--icon", StringComparison.Ordinal);
    }

    private static void AddMatches(List<string> offences, SourceFile file, Regex pattern)
    {
        foreach (Match match in pattern.Matches(file.Text))
        {
            offences.Add($"{file.RelativePath}: {match.Value.Trim()}");
        }
    }

    [GeneratedRegex("\\sstyle\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InlineStyleAttribute();

    [GeneratedRegex("<style", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StyleElement();

    [GeneratedRegex("<script\\b(?![^>]*\\ssrc\\s*=)[^>]*>", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScriptElement();

    [GeneratedRegex("\\son[a-z]+\\s*=", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex EventHandlerAttribute();

    [GeneratedRegex("javascript:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptUrl();

    [GeneratedRegex("(src|href|action|formaction|poster|xlink:href)\\s*=\\s*\"(https?:)?//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExternalOriginAttribute();

    [GeneratedRegex("rel\\s*=\\s*\"(preconnect|dns-prefetch|prefetch|preload|prerender|modulepreload)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ResourceHint();

    [GeneratedRegex("url\\((?<value>[^)]*)\\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CssUrl();

    [GeneratedRegex("https?:|data:|(?<!:)//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CssExternalReference();

    [GeneratedRegex("(-webkit-)?text-security", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TextSecurity();

    [GeneratedRegex("content\\s*:\\s*[^;}]*attr\\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ContentFromAttribute();

    [GeneratedRegex("expression\\(|-moz-binding|(?<![\\w-])behavior\\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LegacyScripting();
}
