// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support;

internal static class RepositoryRoot
{
    private const string SolutionFileName = "SharpVault.slnx";

    private static readonly string[] SkippedDirectories = [".git", ".claude", "bin", "obj", "node_modules"];

    public static string Path { get; } = Locate();

    public static IReadOnlyList<string> EnumerateFiles(string searchPattern) => EnumerateFiles(Path, searchPattern);

    public static IReadOnlyList<string> EnumerateFiles(string root, string searchPattern)
    {
        var found = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            found.AddRange(Directory.EnumerateFiles(directory, searchPattern));
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (!SkippedDirectories.Contains(System.IO.Path.GetFileName(child), StringComparer.Ordinal))
                {
                    pending.Push(child);
                }
            }
        }

        return found.Order(StringComparer.Ordinal).ToList();
    }

    private static string Locate()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("The repository root could not be located.");
    }
}
