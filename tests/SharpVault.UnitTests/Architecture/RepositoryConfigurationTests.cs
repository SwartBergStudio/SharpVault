// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

[Trait("Category", "Security")]
public sealed partial class RepositoryConfigurationTests
{
    private const string WorkflowDirectory = ".github/workflows";
    private const string WorkflowFilter = "*.yml";
    private const string UsesKey = "uses:";
    private const string ListedUsesKey = "- uses:";
    private static readonly string[] ForbiddenFragments =
    [
        "AccountKey",
        "SharedAccessSignature",
        "UseDevelopmentStorage",
        "ConnectionStrings",
        "sig=",
        "DefaultEndpointsProtocol",
        "devstoreaccount1",
        "Password",
        "ClientSecret",
        "KeyMaterial",
    ];

    private static readonly string[] ScriptPatterns = ["*.js", "*.mjs"];

    public static TheoryData<string> ConfigurationFiles
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.json", SearchOption.AllDirectories)
                .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                .Where(path => Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(path).Equals("launchSettings.json", StringComparison.OrdinalIgnoreCase)))
            {
                data.Add(Path.GetRelativePath(RepositoryRoot.Path, file));
            }

            return data;
        }
    }

    [Fact]
    public void ConfigurationFiles_Discovered_IncludeAppsettingsAndLaunchSettings()
    {
        Assert.NotEmpty(ConfigurationFiles);
    }

    [Theory]
    [MemberData(nameof(ConfigurationFiles))]
    public void ConfigurationFile_Content_ContainsNoConnectionStringsOrSecrets(string relativePath)
    {
        var content = File.ReadAllText(Path.Combine(RepositoryRoot.Path, relativePath));

        foreach (var fragment in ForbiddenFragments)
        {
            Assert.DoesNotContain(fragment, content, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void JsModules_AreOnlyTheKnownFour()
    {
        string[] expected =
        [
            "src/SharpVault.UI.Shared/wwwroot/js/clipboard.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-boot.js",
            "src/SharpVault.UI.Shared/wwwroot/js/idle-monitor.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-create.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-crypto.js",
            "src/SharpVault.UI.Shared/wwwroot/js/one-time-link-open.js",
            "src/SharpVault.Web/Components/Layout/ReconnectModal.razor.js",
        ];

        var found = ScriptPatterns
            .SelectMany(pattern => RepositoryRoot.EnumerateFiles(System.IO.Path.Combine(RepositoryRoot.Path, "src"), pattern))
            .Select(path => System.IO.Path.GetRelativePath(RepositoryRoot.Path, path).Replace(System.IO.Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(expected, found);
    }

    [Fact]
    public void ProjectFiles_PackageReferences_CarryNoVersionAttribute()
    {
        var projectFiles = RepositoryRoot.EnumerateFiles("*.csproj");

        var offenders = projectFiles
            .Where(path => System.Xml.Linq.XDocument.Load(path).Descendants("PackageReference").Any(element => element.Attribute("Version") is not null))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.NotEmpty(projectFiles);
        Assert.Empty(offenders);
    }

    [Fact]
    public void Solution_AppHostAndTestProjects_AreListed()
    {
        var solution = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "SharpVault.slnx"));

        Assert.Contains("SharpVault.AppHost.Tests", solution, StringComparison.Ordinal);
        Assert.Contains("SharpVault.UnitTests", solution, StringComparison.Ordinal);
        Assert.Contains("SharpVault.IntegrationTests", solution, StringComparison.Ordinal);
        Assert.Contains("SharpVault.E2ETests", solution, StringComparison.Ordinal);
    }

    [Fact]
    public void Workflows_EveryAction_IsPinnedToAFullCommitSha()
    {
        var references = WorkflowActionReferences().ToList();

        Assert.NotEmpty(references);
        foreach (var reference in references)
        {
            Assert.Matches(PinnedActionReference(), reference);
        }
    }

    [Fact]
    public void CoverageGate_Source_StartsWithTheLicenseHeaderAndKeepsTheAotDirectiveBelowIt()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot.Path, "eng", "CoverageGate.cs"));

        Assert.Equal("// SharpVault - Copyright (c) SwartBergStudio.", lines[0]);
        Assert.Equal("// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.", lines[1]);
        Assert.Equal("#:property PublishAot=false", lines[2]);
    }

    private static IEnumerable<string> WorkflowActionReferences()
    {
        var directory = Path.Combine(RepositoryRoot.Path, WorkflowDirectory);
        foreach (var file in Directory.EnumerateFiles(directory, WorkflowFilter))
        {
            foreach (var line in File.ReadLines(file))
            {
                var reference = ActionReferenceOf(line.Trim());
                if (reference is not null)
                {
                    yield return reference;
                }
            }
        }
    }

    private static string? ActionReferenceOf(string trimmedLine)
    {
        if (trimmedLine.StartsWith(ListedUsesKey, StringComparison.Ordinal))
        {
            return trimmedLine[ListedUsesKey.Length..].Trim();
        }

        return trimmedLine.StartsWith(UsesKey, StringComparison.Ordinal) ? trimmedLine[UsesKey.Length..].Trim() : null;
    }

    [GeneratedRegex(@"^[\w.-]+/[\w.-]+@[0-9a-f]{40}( # v\S+)?\z", RegexOptions.CultureInvariant)]
    private static partial Regex PinnedActionReference();
}
