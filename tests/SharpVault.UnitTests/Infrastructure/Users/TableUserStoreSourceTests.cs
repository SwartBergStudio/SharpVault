// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Users;

[Trait("Category", "Security")]
public sealed class TableUserStoreSourceTests
{
    private static string Source() =>
        File.ReadAllText(Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Infrastructure", "Users", "TableUserStore.cs"));

    [Theory]
    [InlineData("ETag.All")]
    [InlineData("UpsertEntity")]
    [InlineData("DeleteEntity")]
    [InlineData("TableUpdateMode.Merge")]
    public void TableUserStore_Source_NeverUsesETagAllOrUpsert(string fragment)
    {
        Assert.DoesNotContain(fragment, Source(), StringComparison.Ordinal);
    }

    [Fact]
    public void TableUserStore_Source_BuildsFiltersOnlyWithCreateQueryFilter()
    {
        var source = Source();

        Assert.Contains("TableClient.CreateQueryFilter", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PartitionKey eq '", source, StringComparison.Ordinal);
        Assert.DoesNotContain("string.Format", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TableUserStore_Source_EveryFilterHasAPartitionKeyCondition()
    {
        var source = Source();

        var filterLines = source.Split('\n').Where(line => line.Contains("CreateQueryFilter", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(filterLines);
        Assert.All(filterLines, line => Assert.Contains("PartitionKey eq", line, StringComparison.Ordinal));
    }

    [Fact]
    public void InfrastructureSources_NeverUseETagAllOrRawFilterStrings()
    {
        var folder = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Infrastructure");

        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
                     .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                         && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("ETag.All", source, StringComparison.Ordinal);
            Assert.DoesNotContain("PartitionKey eq '", source, StringComparison.Ordinal);
        }
    }
}
