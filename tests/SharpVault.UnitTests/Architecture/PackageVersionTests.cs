// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed class PackageVersionTests
{
    private const string MudBlazorPackage = "MudBlazor";
    private const string UiSharedProject = "SharpVault.UI.Shared.csproj";

    [Fact]
    public void MudBlazor_PackageVersion_IsPinnedTo9_12_0()
    {
        var props = File.ReadAllText(Path.Combine(RepositoryRoot.Path, "Directory.Packages.props"));

        Assert.Contains("<PackageVersion Include=\"MudBlazor\" Version=\"9.12.0\" />", props, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryPackages_MudBlazor_IsReferencedOnlyByUiShared()
    {
        var projects = RepositoryRoot.EnumerateFiles("*.csproj");

        var referencing = projects
            .Where(path => File.ReadAllText(path).Contains($"<PackageReference Include=\"{MudBlazorPackage}\"", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Equal([UiSharedProject], referencing);
    }
}
