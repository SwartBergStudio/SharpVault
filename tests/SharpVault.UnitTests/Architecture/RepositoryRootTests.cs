// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed class RepositoryRootTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("sharpvault-root-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
    }

    [Fact]
    public void EnumerateFiles_WorktreeUnderClaudeFolder_IsNotScanned()
    {
        Touch("src/App/App.csproj");
        Touch("tests/Tests/Tests.csproj");
        Touch(".claude/worktrees/agent-1/src/App/App.csproj");
        Touch(".claude/worktrees/agent-1/tests/Tests/Tests.csproj");

        var found = RepositoryRoot.EnumerateFiles(_root, "*.csproj").Select(path => Path.GetRelativePath(_root, path).Replace('\\', '/'));

        Assert.Equal(["src/App/App.csproj", "tests/Tests/Tests.csproj"], found);
    }

    [Theory]
    [InlineData(".git")]
    [InlineData(".claude")]
    [InlineData("bin")]
    [InlineData("obj")]
    [InlineData("node_modules")]
    [InlineData("src/App/bin")]
    [InlineData("src/App/obj/Release")]
    public void EnumerateFiles_BuildOutputAndToolFolders_AreSkipped(string folder)
    {
        Touch("src/App/App.csproj");
        Touch($"{folder}/Hidden.csproj");

        var found = RepositoryRoot.EnumerateFiles(_root, "*.csproj").Select(path => Path.GetFileName(path));

        Assert.Equal(["App.csproj"], found);
    }

    [Fact]
    public void EnumerateFiles_Repository_FindsTheRealProjectsAndNoneUnderClaude()
    {
        var found = RepositoryRoot.EnumerateFiles("*.csproj");

        Assert.Contains(found, path => path.EndsWith("SharpVault.UI.Shared.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(found, path => path.Contains(Path.DirectorySeparatorChar + ".claude" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        Assert.Equal(found.Count, found.Select(path => Path.GetFileName(path)).Distinct(StringComparer.Ordinal).Count());
    }
}
