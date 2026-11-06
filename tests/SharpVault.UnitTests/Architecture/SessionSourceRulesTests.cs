// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

[Trait("Category", "Security")]
public sealed class SessionSourceRulesTests
{
    private const string SessionPlaceholder = "session}";
    private const string SessionIdPlaceholder = "{sessionId";
    private const string ProblemBodyDeclaration = "class ProblemBody";
    private const string ProblemBodyRecord = "record ProblemBody";
    private const string DtosFolder = "SharpVault.Dtos";
    private const string SourceFilter = "*.cs";
    private const string RazorFilter = "*.razor";
    private const string SourceRoot = "src";

    [Fact]
    public void Razor_SessionIdNeverAppearsInARoute()
    {
        var routes = File.ReadAllText(Path.Combine(RepositoryRoot.Path, SourceRoot, "SharpVault.Dtos", "ApiRoutes.cs"));

        Assert.DoesNotContain(SessionPlaceholder, routes, StringComparison.Ordinal);
        Assert.DoesNotContain(SessionIdPlaceholder, routes, StringComparison.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, SourceRoot), RazorFilter, SearchOption.AllDirectories).Where(IsProductionPath))
        {
            Assert.DoesNotContain("/session/{", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sources_NoTypeNamedProblemBodyOutsideDtos()
    {
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, SourceRoot), SourceFilter, SearchOption.AllDirectories).Where(IsProductionPath))
        {
            var text = File.ReadAllText(file);

            Assert.DoesNotContain(ProblemBodyDeclaration, text, StringComparison.Ordinal);
            Assert.DoesNotContain(ProblemBodyRecord, text, StringComparison.Ordinal);
        }
    }

    private static bool IsProductionPath(string file) =>
        !file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !file.Contains(DtosFolder, StringComparison.Ordinal);
}
