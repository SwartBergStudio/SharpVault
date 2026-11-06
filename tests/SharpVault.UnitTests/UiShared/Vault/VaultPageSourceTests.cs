// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Vault;

public sealed partial class VaultPageSourceTests
{
    private static string PagesRoot => Path.Combine(RepositoryRoot.Path, "src", "SharpVault.UI.Shared", "Pages");

    [GeneratedRegex(@"(^|\s)@inject\b|\[Inject\]", RegexOptions.Multiline)]
    private static partial Regex InjectionPattern();

    [GeneratedRegex(@"^@attribute\s+\[Authorize\]\s*$", RegexOptions.Multiline)]
    private static partial Regex AuthorizeAttributePattern();

    [GeneratedRegex(@"^@page\s+""", RegexOptions.Multiline)]
    private static partial Regex PageDirectivePattern();

    private static List<string> VaultPageFiles()
    {
        var files = Directory.EnumerateFiles(Path.Combine(PagesRoot, "Vault"), "*.razor", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(PagesRoot, "Vault*.razor", SearchOption.TopDirectoryOnly))
            .Order(StringComparer.Ordinal)
            .ToList();
        return files;
    }

    private static string Relative(string path) => Path.GetRelativePath(PagesRoot, path).Replace('\\', '/');

    [Fact]
    [Trait("Category", "Security")]
    public void VaultPages_Sources_InjectNothing()
    {
        var files = VaultPageFiles();

        var offenders = files
            .Where(path => InjectionPattern().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.True(files.Count >= 7, $"Expected at least 7 vault pages, found {files.Count}.");
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void VaultPages_Sources_AllRequireAuthorization()
    {
        var files = VaultPageFiles();

        var offenders = files
            .Select(path => (Path: path, Source: File.ReadAllText(path)))
            .Where(file => PageDirectivePattern().IsMatch(file.Source) && !AuthorizeAttributePattern().IsMatch(file.Source))
            .Select(file => Relative(file.Path))
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Fact]
    public void VaultPages_Sources_EachDeclaresARoute()
    {
        var files = VaultPageFiles();

        var routeless = files
            .Where(path => !PageDirectivePattern().IsMatch(File.ReadAllText(path)))
            .Select(Relative)
            .ToList();

        Assert.Empty(routeless);
    }

    [Theory]
    [InlineData("@page \"/x\"\n@inject IFoo Foo", true)]
    [InlineData("@page \"/x\"\n@inject  IFoo Foo", true)]
    [InlineData("[Inject]\nprivate IFoo Foo { get; set; }", true)]
    [InlineData("@page \"/x\"\n<VaultBrowser />", false)]
    [InlineData("<p>mail me @injection</p>", false)]
    public void InjectionPattern_Source_IsRecognisedOnlyForRealInjection(string source, bool expected)
    {
        Assert.Equal(expected, InjectionPattern().IsMatch(source));
    }
}
