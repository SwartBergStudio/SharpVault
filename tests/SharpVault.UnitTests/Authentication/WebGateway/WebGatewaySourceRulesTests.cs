// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using SharpVault.Authentication.WebGateway;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Authentication.WebGateway;

[Trait("Category", "Security")]
public sealed class WebGatewaySourceRulesTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void AppSettings_NeverContainTheWebGatewayKey()
    {
        var sourceRoot = Path.Combine(RepositoryRoot.Path, "src");
        var files = Directory.EnumerateFiles(sourceRoot, "appsettings*.json", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(files);
        Assert.All(files, path => Assert.False(HoldsGatewayKey(path), Path.GetRelativePath(RepositoryRoot.Path, path)));
    }

    private static bool HoldsGatewayKey(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty(WebGatewayOptions.SectionName, out var section))
        {
            return false;
        }

        return section.TryGetProperty(nameof(WebGatewayOptions.Key), out var key)
            && key.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(key.GetString());
    }
}
