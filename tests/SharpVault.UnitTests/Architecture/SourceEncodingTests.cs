// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed class SourceEncodingTests
{
    private static readonly string[] Roots = ["src", "tests", "eng"];
    private static readonly string[] Extensions = [".cs", ".razor", ".css", ".js", ".mjs", ".json", ".csproj", ".props", ".targets"];
    private static readonly string[] IgnoredSegments = ["bin", "obj", "node_modules"];

    [Fact]
    public void SourceFiles_AreUtf8WithoutByteOrderMark()
    {
        var files = Roots
            .Select(root => Path.Combine(RepositoryRoot.Path, root))
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            .Where(path => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .Where(path => !IsInIgnoredDirectory(path))
            .ToList();

        var withBom = files.Where(HasByteOrderMark).Select(path => Path.GetRelativePath(RepositoryRoot.Path, path)).ToList();

        Assert.NotEmpty(files);
        Assert.True(withBom.Count == 0, "Files with a byte order mark: " + string.Join(", ", withBom));
    }

    private static bool IsInIgnoredDirectory(string path)
    {
        var relative = Path.GetRelativePath(RepositoryRoot.Path, path);
        return relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .SkipLast(1)
            .Any(segment => IgnoredSegments.Contains(segment, StringComparer.Ordinal));
    }

    private static bool HasByteOrderMark(string path)
    {
        Span<byte> head = stackalloc byte[3];
        using var stream = File.OpenRead(path);
        var read = stream.Read(head);
        return read == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
    }
}
