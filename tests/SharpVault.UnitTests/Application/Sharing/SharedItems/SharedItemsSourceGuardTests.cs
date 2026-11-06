// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Application.Sharing.SharedItems;

[Trait("Category", "Security")]
public sealed partial class SharedItemsSourceGuardTests
{
    private static readonly string[] DefinitionFiles =
    [
        "SecretAccessExtensions.cs",
        "SecretAccessPolicy.cs",
        "ISecretAccessPolicy.cs",
        "ShareAccessResolver.cs",
    ];

    [GeneratedRegex(@"ResolveSharedAsync\(|RequireAsync\(\s*request\.SharedOwnerId|ForSecretAsync\(", RegexOptions.CultureInvariant)]
    private static partial Regex ResolvedSecretGrant();

    [GeneratedRegex(@"ListFolderGrantsToRecipientAsync\(|ForFolderAsync\(", RegexOptions.CultureInvariant)]
    private static partial Regex ResolvedFolderGrant();

    [GeneratedRegex(@"\bGetSecretAsync\(", RegexOptions.CultureInvariant)]
    private static partial Regex ActiveSecretRead();

    [GeneratedRegex(@"\b(ListFoldersAsync|GetFolderAsync)\(", RegexOptions.CultureInvariant)]
    private static partial Regex ActiveFolderRead();

    [GeneratedRegex(@"\b(GetSecretAsync|GetFolderAsync|ListFoldersAsync|ListSecretsAsync)\(", RegexOptions.CultureInvariant)]
    private static partial Regex AnyActiveRowRead();

    private static IEnumerable<string> Sources(string relativeRoot) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, relativeRoot), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string Features => Path.Combine("src", "SharpVault.Application", "Features");

    private static string SharedItems => Path.Combine(Features, "Sharing", "SharedItems");

    [Fact]
    public void FeatureSources_UsingAResolvedSecretGrant_AlsoReadTheActiveSecretRow()
    {
        var files = Sources(Features).ToList();

        var offenders = files
            .Where(path => !DefinitionFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => ResolvedSecretGrant().IsMatch(file.Text) && !ActiveSecretRead().IsMatch(file.Text))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file.Path))
            .ToList();

        Assert.True(files.Count > 50, "The feature sources were not found.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void FeatureSources_UsingResolvedFolderGrants_AlsoReadTheActiveFolderRows()
    {
        var offenders = Sources(Features)
            .Where(path => !DefinitionFiles.Contains(Path.GetFileName(path), StringComparer.Ordinal))
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => ResolvedFolderGrant().IsMatch(file.Text) && !ActiveFolderRead().IsMatch(file.Text))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file.Path))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedItemHandlers_Always_ReadAnActiveRow()
    {
        var handlers = Sources(SharedItems)
            .Select(path => (Path: path, Text: File.ReadAllText(path)))
            .Where(file => file.Text.Contains("IRequestHandler<", StringComparison.Ordinal))
            .ToList();

        var offenders = handlers
            .Where(file => !AnyActiveRowRead().IsMatch(file.Text))
            .Select(file => Path.GetRelativePath(RepositoryRoot.Path, file.Path))
            .ToList();

        Assert.True(handlers.Count >= 4, "The shared item handlers were not found.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void SharedItemHandlers_Never_ReadTheRecycledRows()
    {
        var offenders = Sources(SharedItems)
            .Where(path => File.ReadAllText(path).Contains("Recycled", StringComparison.Ordinal))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }
}
