// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed class VaultComponentSourceTests
{
    private const string LoadableBase = "LoadableComponentBase";
    private const string DataStateView = "<DataStateView";

    private static string ComponentsRoot => Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "Components", "Vault");

    [Fact]
    [Trait("Category", "Security")]
    public void VaultComponents_NoLoadableMissingDataStateView()
    {
        var loadable = Directory.EnumerateFiles(ComponentsRoot, "*.razor")
            .Where(path => File.ReadAllText(path).Contains($"@inherits {LoadableBase}", StringComparison.Ordinal))
            .ToList();

        var missing = loadable
            .Where(path => !File.ReadAllText(path).Contains(DataStateView, StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(loadable.Count >= 6, $"Expected at least 6 loadable vault components, found {loadable.Count}.");
        Assert.Empty(missing);
    }
}
