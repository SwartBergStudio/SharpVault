// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

namespace SharpVault.UnitTests.Support.Theme;

internal sealed record SourceFile(string FullPath, string RelativePath, string Text);

internal static class UiSourceFiles
{
    public const string UiSharedProject = "src/SharpVault.UI.Shared";
    public const string WebProject = "src/SharpVault.Web";

    private static readonly string[] ProjectRoots = [UiSharedProject, WebProject];
    private static readonly string[] SkippedSegments = ["bin", "obj"];

    public static IReadOnlyList<SourceFile> WithExtension(string extension) =>
        Enumerate(ProjectRoots, extension);

    public static IReadOnlyList<SourceFile> UiSharedWithExtension(string extension) =>
        Enumerate([UiSharedProject], extension);

    public static IReadOnlyList<string> AllUiSharedWebRootFiles()
    {
        var webRoot = Path.Combine(RepositoryRoot.Path, UiSharedProject, "wwwroot");
        return Directory.EnumerateFiles(webRoot, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    public static string Absolute(string relativePath) =>
        Path.Combine(RepositoryRoot.Path, relativePath);

    private static List<SourceFile> Enumerate(IEnumerable<string> roots, string extension)
    {
        var files = new List<SourceFile>();
        foreach (var root in roots)
        {
            var directory = Path.Combine(RepositoryRoot.Path, root);
            foreach (var path in Directory.EnumerateFiles(directory, "*" + extension, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(RepositoryRoot.Path, path).Replace('\\', '/');
                var segments = relative.Split('/');
                if (segments.Any(segment => SkippedSegments.Contains(segment, StringComparer.Ordinal)))
                {
                    continue;
                }

                if (!path.EndsWith(extension, StringComparison.Ordinal))
                {
                    continue;
                }

                files.Add(new SourceFile(path, relative, File.ReadAllText(path)));
            }
        }

        return files.OrderBy(file => file.RelativePath, StringComparer.Ordinal).ToList();
    }
}
