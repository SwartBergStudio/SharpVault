// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed partial class ComponentSourceRulesTests
{
    private const int MaxRazorLines = 200;
    private const string UiShared = "SharpVault.UI.Shared";
    private const string CommonFolder = "Components/Common";
    private const string StaticPagesFolder = "Pages";

    private static readonly string[] MudProviders = ["MudThemeProvider", "MudPopoverProvider", "MudDialogProvider", "MudSnackbarProvider"];

    private static readonly Dictionary<string, string[]> AllowedMudOutsideCommon = new(StringComparer.Ordinal)
    {
        ["Layout/MainLayout.razor"] = MudProviders,
    };

    private static string UiSharedRoot => Path.Combine(RepositoryRoot.Path, "src", UiShared);

    private static string WebRoot => Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Web");

    private static List<string> RazorFiles() =>
        Directory.EnumerateFiles(UiSharedRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();

    private static bool IsBuildOutput(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal);

    private static List<string> PageFiles() =>
        RazorFiles().Where(path => IsInside(path, StaticPagesFolder))
            .Concat(Directory.EnumerateFiles(Path.Combine(WebRoot, "Components", "Pages"), "*.razor").Where(path => !IsBuildOutput(path)))
            .ToList();

    private static string PageLogic(string razor)
    {
        var marker = razor.IndexOf("@code", StringComparison.Ordinal);
        if (marker < 0)
        {
            return string.Empty;
        }

        var open = razor.IndexOf('{', marker);
        var close = razor.LastIndexOf('}');
        if (open < 0 || close <= open)
        {
            return string.Empty;
        }

        var code = WhitespaceRun().Replace(razor[(open + 1)..close], " ").Trim();
        foreach (var allowed in new[] { ParameterAttribute(), AutoProperty(), LoadAsyncExpression() })
        {
            code = allowed.Replace(code, string.Empty);
        }

        return WhitespaceRun().Replace(code, " ").Trim();
    }

    private static List<string> WebMarkupFiles() =>
        Directory.EnumerateFiles(WebRoot, "*.razor", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .ToList();

    private static string WebRelative(string path) => "Web/" + Path.GetRelativePath(WebRoot, path).Replace('\\', '/');

    private static string Relative(string path) => Path.GetRelativePath(UiSharedRoot, path).Replace('\\', '/');

    private static bool IsInside(string path, string folder) => Relative(path).StartsWith(folder + "/", StringComparison.Ordinal);

    [Fact]
    public void RazorFiles_UiSharedAndWeb_AreAtMost200Lines()
    {
        var uiFiles = RazorFiles();
        var webFiles = WebMarkupFiles();

        var offenders = uiFiles
            .Where(path => File.ReadAllLines(path).Length > MaxRazorLines)
            .Select(Relative)
            .Concat(webFiles
                .Where(path => File.ReadAllLines(path).Length > MaxRazorLines)
                .Select(WebRelative))
            .ToList();

        Assert.NotEmpty(uiFiles);
        Assert.NotEmpty(webFiles);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Pages_OnlyRouteAndCompose_NeverInjectHttpClientOrStorage()
    {
        var files = PageFiles();

        var offenders = files
            .Where(path => ForbiddenInPages().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Pages_OnlyRouteAndCompose_HaveNoCodeBeyondParameterWiring()
    {
        var files = PageFiles();

        var offenders = files
            .Select(path => (Path: path, Leftover: PageLogic(File.ReadAllText(path))))
            .Where(result => result.Leftover.Length > 0)
            .Select(result => $"{Relative(result.Path)}: {result.Leftover}")
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("@code { [Parameter] public string? Name { get; set; } }", "")]
    [InlineData("@code { private int _count; }", "private int _count;")]
    public void PageLogic_Source_ReportsOnlyCodeBeyondParameterWiring(string source, string expected)
    {
        Assert.Equal(expected, PageLogic(source));
    }

    private static List<string> MudViolations(string relativePath, string source)
    {
        if (relativePath.StartsWith(CommonFolder + "/", StringComparison.Ordinal))
        {
            return [];
        }

        var allowed = AllowedMudOutsideCommon.GetValueOrDefault(relativePath, []);
        return MudTag().Matches(source)
            .Select(match => match.Groups[1].Value)
            .Where(tag => !allowed.Contains(tag))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Markup_OutsideComponentsCommon_UsesMudBlazorOnlyThroughTheSvWrappers()
    {
        var files = RazorFiles();
        var webFiles = WebMarkupFiles();

        var offenders = files
            .SelectMany(path => MudViolations(Relative(path), File.ReadAllText(path)).Select(tag => Relative(path) + " uses " + tag))
            .Concat(webFiles.SelectMany(path => MudViolations(WebRelative(path), File.ReadAllText(path)).Select(tag => WebRelative(path) + " uses " + tag)))
            .ToList();

        Assert.NotEmpty(webFiles);
        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("Components/Vault/SecretForm.razor", "<MudStack Spacing=\"3\"></MudStack>", "MudStack")]
    [InlineData("Pages/Account.razor", "<MudText Typo=\"Typo.h4\">x</MudText>", "MudText")]
    public void MudViolations_ReintroducedUse_IsReported(string path, string source, string expected)
    {
        Assert.Equal([expected], MudViolations(path, source));
    }

    [Theory]
    [InlineData("Components/Common/SvText.razor", "<MudText>x</MudText>")]
    [InlineData("Layout/MainLayout.razor", "<MudThemeProvider /><MudPopoverProvider /><MudDialogProvider /><MudSnackbarProvider />")]
    public void MudViolations_AllowedUse_IsNotReported(string path, string source)
    {
        Assert.Empty(MudViolations(path, source));
    }

    [Fact]
    public void Components_Common_DoNotImportPagesNamespace()
    {
        var files = RazorFiles().Where(path => IsInside(path, CommonFolder)).ToList();

        var offenders = files
            .Where(path => File.ReadAllText(path).Contains("SharpVault.UI.Shared.Pages", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Pages_DoNotUseIconsMaterialDirectly()
    {
        var files = RazorFiles().Where(path => IsInside(path, StaticPagesFolder)).ToList();

        var offenders = files
            .Where(path => File.ReadAllText(path).Contains("Icons.Material", StringComparison.Ordinal))
            .Select(Relative)
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [GeneratedRegex(@"<(Mud\w+)\b", RegexOptions.CultureInvariant)]
    private static partial Regex MudTag();

    [GeneratedRegex(@"@inject\s+(HttpClient|IJSRuntime|IHttpClientFactory|TableServiceClient)\b|new HttpClient", RegexOptions.CultureInvariant)]
    private static partial Regex ForbiddenInPages();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\[(?:Parameter|CascadingParameter|SupplyParameterFromQuery)(?:\([^\]]*\))?(?:,\s*EditorRequired)?\]", RegexOptions.CultureInvariant)]
    private static partial Regex ParameterAttribute();

    [GeneratedRegex(@"(?:public|private|protected|internal) [\w<>?\[\],. ]+ \w+ \{ get; (?:set|init); \}(?: = [^;]+;)?", RegexOptions.CultureInvariant)]
    private static partial Regex AutoProperty();

    [GeneratedRegex(@"protected override Task<ApiResponse<\w+>> LoadAsync\(CancellationToken \w+\) => [\w.]+\([\w, ]*\);", RegexOptions.CultureInvariant)]
    private static partial Regex LoadAsyncExpression();
}
