// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components;
using SharpVault.UI.Shared.Pages;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.UiShared.Components;

public sealed partial class ComponentLiteralParameterRulesTests
{
    private static readonly Lazy<ILookup<string, Type>> ComponentsByName = new(LoadComponents);

    [GeneratedRegex(@"<(?<tag>[A-Z]\w*)\b(?<attributes>(?:[^<>""]|""[^""]*"")*)")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"(?<![\w@:.])(?<name>[A-Z]\w*)=""\k<name>""")]
    private static partial Regex SelfNamedAttributePattern();

    private static ILookup<string, Type> LoadComponents()
    {
        var assemblies = new[]
        {
            typeof(VaultPage).Assembly,
            Assembly.Load(new AssemblyName("SharpVault.Web")),
            typeof(MudBlazor.MudText).Assembly,
        };
        return assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => typeof(IComponent).IsAssignableFrom(type))
            .ToLookup(type => type.Name, StringComparer.Ordinal);
    }

    private static bool IsStringParameter(string component, string parameter) =>
        ComponentsByName.Value[component].Any(type =>
            type.GetProperty(parameter, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.PropertyType == typeof(string));

    internal static List<string> LiteralStringParameters(string path, string razor)
    {
        var offenders = new List<string>();
        foreach (Match tag in TagPattern().Matches(razor))
        {
            var component = tag.Groups["tag"].Value;
            foreach (Match attribute in SelfNamedAttributePattern().Matches(tag.Groups["attributes"].Value))
            {
                var name = attribute.Groups["name"].Value;
                if (IsStringParameter(component, name))
                {
                    offenders.Add($"{path}: <{component} {name}=\"{name}\"> passes the text \"{name}\"; use {name}=\"@{name}\"");
                }
            }
        }

        return offenders;
    }

    private static List<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void Markup_StringParameterNamedAfterItself_IsNotPassedAsLiteralText()
    {
        var files = SourceFiles();

        var offenders = files
            .SelectMany(path => LiteralStringParameters(Path.GetRelativePath(RepositoryRoot.Path, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(path)))
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("<VaultBrowser FolderId=\"@FolderId\" Search=\"Search\" />", "Search")]
    [InlineData("<VaultBrowser Unfiled=\"true\" Search=\"Search\" />", "Search")]
    [InlineData("<SvText Kind=\"Kind\" Id=\"Id\">x</SvText>", "Id")]
    public void LiteralStringParameters_StringParameterNamedAfterItself_IsReported(string source, string expected)
    {
        var offenders = LiteralStringParameters("Pages/X.razor", source);

        Assert.Contains(offenders, offender => offender.Contains($"{expected}=\"{expected}\"", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("<VaultBrowser Search=\"@Search\" />")]
    [InlineData("<DataStateView TValue=\"X\" Value=\"Value\" State=\"State\" />")]
    [InlineData("<SecretDetailView SecretId=\"SecretId\" />")]
    [InlineData("<RecycleBinTable Items=\"Items\" />")]
    [InlineData("<SvButton OnClick=\"@(() => a > b)\" Disabled=\"Disabled\" />")]
    public void LiteralStringParameters_NonStringOrBound_IsNotReported(string source)
    {
        Assert.Empty(LiteralStringParameters("Pages/X.razor", source));
    }
}
