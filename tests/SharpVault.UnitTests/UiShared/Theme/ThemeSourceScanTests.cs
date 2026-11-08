// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support.Theme;

namespace SharpVault.UnitTests.UiShared.Theme;

public sealed partial class ThemeSourceScanTests
{
    private const string FaviconPath = "src/SharpVault.UI.Shared/wwwroot/brand/favicon.svg";

    [Fact]
    [Trait("Category", "Security")]
    public void RazorSources_UiProjects_ContainNoInlineStyleScriptHandlersOrExternalOrigins()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            AddMatches(offences, file, InlineStyleAttribute());
            AddMatches(offences, file, StyleElement());
            AddMatches(offences, file, InlineScriptElement());
            offences.AddRange(EventHandlerAttributes.Find(file.Text).Select(offence => $"{file.RelativePath}: {offence}"));
            AddMatches(offences, file, JavascriptUrl());
            AddMatches(offences, file, ExternalOriginAttribute());
            AddMatches(offences, file, ResourceHint());
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("<div onClick=\"x()\">", true)]
    [InlineData("<div title=\"a > b\" onclick=\"x()\">", true)]
    [InlineData("<SvButton Id=\"a\" OnClick=\"Save\">", false)]
    [InlineData("<p>use onclick= in text</p>", false)]
    public void EventHandlerRule_HtmlHandlersAreCaughtAndComponentParametersAreNot(string sample, bool flagged)
    {
        Assert.Equal(flagged, EventHandlerAttributes.Find(sample).Count > 0);
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
    public void RazorSources_InteractiveElements_HaveIds()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".razor"))
        {
            foreach (var tag in RazorTags.Find(file.Text).Where(tag => !RazorTags.HasAttribute(tag, "id") && !RazorTags.HasAttribute(tag, "Id")))
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
                if (!IsComplete(tag))
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
    public void SecretInputRule_SampleMarkup_IsRecognisedAsCompleteOrIncomplete(string markup, bool expectedComplete)
    {
        var tag = Assert.Single(RazorTags.Find(markup));

        Assert.True(IsSecretInput(tag));
        Assert.Equal(expectedComplete, IsComplete(tag));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_UiProjects_ContainNoExternalOriginsImportsValueSelectorsOrScriptingFeatures()
    {
        var offences = new List<string>();
        foreach (var file in UiSourceFiles.WithExtension(".css"))
        {
            AddMatches(offences, file, CssExternalReference());
            AddMatches(offences, file, TextSecurity());
            AddMatches(offences, file, ContentFromAttribute());
            AddMatches(offences, file, LegacyScripting());
            if (file.Text.Contains("@import", StringComparison.OrdinalIgnoreCase))
            {
                offences.Add($"{file.RelativePath}: @import");
            }

            if (file.Text.Contains("[value", StringComparison.OrdinalIgnoreCase))
            {
                offences.Add($"{file.RelativePath}: [value selector");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_Print_HidesRevealedValues()
    {
        var css = ThemeStylesheet.Load();
        var start = css.IndexOf("@media print {", StringComparison.Ordinal);
        Assert.True(start >= 0, "app.css has no print rule.");
        var end = css.IndexOf("\n}", start, StringComparison.Ordinal);
        Assert.True(end > start, "The print rule has no end.");
        Assert.Contains(".sv-secret[data-state=\"revealed\"] .sv-secret__value { visibility: hidden; }", css[start..end], StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void StyleSheets_Disconnected_HidesSensitiveValues()
    {
        Assert.Matches(@"html\[data-sv-disconnected\] \[data-sv-sensitive\] \{\s*visibility: hidden;", ThemeStylesheet.Load());
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UiSharedWebRoot_JavaScriptFiles_AreExactlyThePinnedModules()
    {
        string[] pinned =
        [
            "src/SharpVault.UI.Shared/wwwroot/js/clipboard.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-boot.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-monitor.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-create.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-crypto.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-open.js",
        ];

        var actual = UiSourceFiles.AllUiSharedWebRootFiles()
            .Where(file => string.Equals(Path.GetExtension(file), ".js", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(pinned, actual);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void PinnedJavaScript_Sources_UseNoDynamicCodeNetworkOrMarkupInjection()
    {
        string[] pinnedModules =
        [
            "src/SharpVault.UI.Shared/wwwroot/js/clipboard.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-boot.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-monitor.js",
        ];
        string[] forbidden = ["eval(", "new Function", "innerHTML", "outerHTML", "document.write", "fetch(", "XMLHttpRequest", "WebSocket", "sendBeacon", "import(", "http:", "https:", "document.cookie"];

        var offences = new List<string>();
        foreach (var relativePath in pinnedModules)
        {
            var text = File.ReadAllText(UiSourceFiles.Absolute(relativePath));
            offences.AddRange(forbidden.Where(token => text.Contains(token, StringComparison.Ordinal)).Select(token => $"{relativePath}: {token}"));
        }

        Assert.Empty(offences);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void UiSharedWebRoot_Files_HaveOnlyExpectedTypes()
    {
        var allowed = new[] { ".css", ".js", ".woff2", ".txt", ".svg" };

        var unexpected = UiSourceFiles.AllUiSharedWebRootFiles()
            .Where(file => !allowed.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            .ToList();

        Assert.True(unexpected.Count == 0, "Unexpected web root files: " + string.Join(", ", unexpected));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Favicon_File_ContainsNoScriptStyleOrLinks()
    {
        var path = UiSourceFiles.Absolute(FaviconPath);
        var bytes = File.ReadAllBytes(path);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        foreach (var forbidden in new[] { "<script", "<style", "href", "<foreignObject", "javascript:" })
        {
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Empty(EventHandlerAttributes.Find(text));
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

    private static bool IsComplete(RazorTag tag) =>
        RazorTags.AttributeValue(tag, "spellcheck") == "false"
        && RazorTags.AttributeValue(tag, "autocapitalize") == "off"
        && RazorTags.AttributeValue(tag, "autocorrect") == "off"
        && RazorTags.HasAttribute(tag, "autocomplete");

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

    [GeneratedRegex("javascript:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptUrl();

    [GeneratedRegex("(src|href|action|formaction|poster|xlink:href)\\s*=\\s*\"(https?:)?//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ExternalOriginAttribute();

    [GeneratedRegex("rel\\s*=\\s*\"(preconnect|dns-prefetch|prefetch|preload|prerender|modulepreload)\"", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ResourceHint();

    [GeneratedRegex("https?:|data:|(?<!:)//", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CssExternalReference();

    [GeneratedRegex("(-webkit-)?text-security", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TextSecurity();

    [GeneratedRegex("content\\s*:\\s*[^;}]*attr\\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ContentFromAttribute();

    [GeneratedRegex("expression\\(|-moz-binding|(?<![\\w-])behavior\\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LegacyScripting();
}
