// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Infrastructure.Vaults;

[Trait("Category", "Security")]
public sealed partial class VaultStoreSourceTests
{
    private static readonly string[] StoreFolders = ["Vaults", "Audit", "Maintenance", "Sharing"];

    [GeneratedRegex("""(?<!CreateQueryFilter\(\s*)(?<!\$)\$?"[^"\r\n]*\b(PartitionKey|RowKey|FolderKey|DeletedAt|DeletionBatchId)\s+(eq|ne|gt|ge|lt|le)\b""", RegexOptions.CultureInvariant)]
    private static partial Regex HandBuiltFilter();

    [GeneratedRegex("""\b(PartitionKey|RowKey)\s+(eq|ne|gt|ge|lt|le)\s*'""", RegexOptions.CultureInvariant)]
    private static partial Regex LiteralInFilter();

    [GeneratedRegex(@"ETag\.All|new ETag\(""\*""\)|UpsertEntity|ETag\s*=\s*ETag\.All|\.Wildcard", RegexOptions.CultureInvariant)]
    private static partial Regex Overwrite();

    private static IEnumerable<(string Name, string Text)> Sources(string folder)
    {
        var directory = Path.Combine(RepositoryRoot.Path, "src", "SharpVault.Infrastructure", folder);
        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Select(path => (Path.GetFileName(path), File.ReadAllText(path)));
    }

    [Fact]
    public void VaultStoreSources_UseCreateQueryFilterOnly()
    {
        var sources = Sources("Vaults").ToList();

        var offenders = sources
            .Where(source => HandBuiltFilter().IsMatch(source.Text) || LiteralInFilter().IsMatch(source.Text))
            .Select(source => source.Name)
            .ToList();

        Assert.Contains(sources, source => source.Name == "TableVaultStore.cs");
        Assert.Contains("CreateQueryFilter(", sources.Single(source => source.Name == "TableVaultStore.cs").Text, StringComparison.Ordinal);
        Assert.Empty(offenders);
    }

    [Fact]
    public void VaultStoreSources_NeverUseETagAll()
    {
        var offenders = StoreFolders
            .SelectMany(Sources)
            .Where(source => Overwrite().IsMatch(source.Text))
            .Select(source => source.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void VaultStoreSources_NeverQueryWithoutAPartitionKey()
    {
        var text = Sources("Vaults").Single(source => source.Name == "TableVaultStore.cs").Text;

        var filters = Regex.Matches(text, @"CreateQueryFilter\(\s*\$""(?<body>[^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))
            .Select(match => match.Groups["body"].Value)
            .ToList();

        Assert.NotEmpty(filters);
        Assert.All(filters, body => Assert.StartsWith("PartitionKey eq {", body, StringComparison.Ordinal));
    }

    [Fact]
    public void ShareStores_Sources_UseCreateQueryFilterAndNoWildcard()
    {
        var sources = Sources("Sharing").ToList();

        var offenders = sources
            .Where(source => HandBuiltFilter().IsMatch(source.Text) || LiteralInFilter().IsMatch(source.Text) || Overwrite().IsMatch(source.Text))
            .Select(source => source.Name)
            .ToList();

        var store = sources.Single(source => source.Name == "TableShareInboxStore.cs").Text;
        Assert.Contains("CreateQueryFilter(", store, StringComparison.Ordinal);
        Assert.Empty(offenders);
    }

    [Fact]
    public void ShareStores_Sources_NeverQueryWithoutAPartitionKey()
    {
        var texts = new[]
        {
            Sources("Sharing").Single(source => source.Name == "TableShareInboxStore.cs").Text,
            Sources("Vaults").Single(source => source.Name == "TableVaultStore.cs").Text,
        };

        var filters = texts
            .SelectMany(text => Regex.Matches(text, @"CreateQueryFilter\(\s*\$""(?<body>[^""]*)""", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2)))
            .Select(match => match.Groups["body"].Value)
            .ToList();

        Assert.True(filters.Count >= 4);
        Assert.All(filters, body => Assert.StartsWith("PartitionKey eq {", body, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("var f = $\"PartitionKey eq '{owner}' and RowKey ge 's_'\";")]
    [InlineData("var f = \"PartitionKey eq \" + owner;")]
    [InlineData("var f = TableClient.CreateQueryFilter($\"PartitionKey eq {p}\") + \" and RowKey ge 'x'\";")]
    public void HandBuiltFilterRule_ConcatenatedOrLiteralFilters_AreCaught(string source)
    {
        Assert.True(HandBuiltFilter().IsMatch(source) || LiteralInFilter().IsMatch(source));
    }

    [Theory]
    [InlineData("var f = TableClient.CreateQueryFilter($\"PartitionKey eq {p} and RowKey ge {lower}\");")]
    [InlineData("var f = TableClient.CreateQueryFilter(\n    $\"PartitionKey eq {p}\");")]
    public void HandBuiltFilterRule_CreateQueryFilterCalls_AreNotFlagged(string source)
    {
        Assert.DoesNotMatch(HandBuiltFilter(), source);
        Assert.DoesNotMatch(LiteralInFilter(), source);
    }

    [Theory]
    [InlineData("table.UpsertEntityAsync(entity);")]
    [InlineData("table.UpdateEntityAsync(entity, ETag.All);")]
    [InlineData("var e = new ETag(\"*\");")]
    public void OverwriteRule_Wildcards_AreCaught(string source)
    {
        Assert.Matches(Overwrite(), source);
    }
}
