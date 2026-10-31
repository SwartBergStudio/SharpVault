// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

[Trait("Category", "Security")]
public sealed class RepositoryConfigurationTests
{
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
    public void ProjectFiles_PackageReferences_CarryNoVersionAttribute()
    {
        var projectFiles = Directory.EnumerateFiles(RepositoryRoot.Path, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

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
    }
}
