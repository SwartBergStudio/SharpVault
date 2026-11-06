// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Text.RegularExpressions;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed partial class UiBoundaryTests
{
    private const string UiShared = "SharpVault.UI.Shared";
    private const string Web = "SharpVault.Web";
    private const string Api = "SharpVault.Api";
    private const string Domain = "SharpVault.Domain";
    private const string Application = "SharpVault.Application";
    private const string Infrastructure = "SharpVault.Infrastructure";
    private const string EndpointsSuffix = "Endpoints";
    private const string ClientMarker = "Client";

    private static readonly string[] ForbiddenNamespaces = [Domain, Application, Infrastructure];

    [Fact]
    public void UiSharedAndWebSources_Imports_ContainNoApplicationDomainOrInfrastructureNamespace()
    {
        var files = SourceFiles(UiShared, "*.cs").Concat(SourceFiles(UiShared, "*.razor"))
            .Concat(SourceFiles(Web, "*.cs")).Concat(SourceFiles(Web, "*.razor"))
            .ToList();

        var offenders = files
            .Where(path => File.ReadLines(path).Any(IsForbiddenImport))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    [Fact]
    public void UiSharedAndWebAssemblies_References_ContainNoApplicationDomainOrInfrastructure()
    {
        var offenders = new[] { UiShared, Web }
            .SelectMany(name => Assembly.Load(new AssemblyName(name)).GetReferencedAssemblies()
                .Select(reference => (Assembly: name, Reference: reference.Name ?? string.Empty)))
            .Where(item => ForbiddenNamespaces.Contains(item.Reference))
            .Select(item => item.Assembly + " -> " + item.Reference)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ApiEndpoints_Signatures_ContainNoDomainType()
    {
        var endpointTypes = Assembly.Load(new AssemblyName(Api)).GetTypes()
            .Where(type => type.IsAbstract && type.IsSealed && type.Name.EndsWith(EndpointsSuffix, StringComparison.Ordinal))
            .ToList();

        var offenders = endpointTypes
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)
                .SelectMany(Flatten)
                .Where(IsDomainOrApplicationType)
                .Select(type => method.DeclaringType!.Name + "." + method.Name + " uses " + type.FullName))
            .ToList();

        Assert.NotEmpty(endpointTypes);
        Assert.Empty(offenders);
    }

    [Fact]
    public void ApiClients_Sources_ReferenceRoutesOnlyThroughApiRoutes()
    {
        var files = ClientSourceFiles();

        var offenders = files
            .Where(path => RouteLiteral().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.NotEmpty(files);
        Assert.Empty(offenders);
    }

    private static List<string> ClientSourceFiles()
    {
        var folder = Path.Combine(RepositoryRoot.Path, "src", UiShared, "Services");

        return Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path).Contains(ClientMarker, StringComparison.Ordinal))
            .ToList();
    }

    private static List<string> SourceFiles(string project, string pattern) =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src", project), pattern, SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToList();

    private static bool IsForbiddenImport(string line)
    {
        var trimmed = line.Trim();
        var isImport = trimmed.StartsWith("using ", StringComparison.Ordinal) || trimmed.StartsWith("@using ", StringComparison.Ordinal);
        if (!isImport)
        {
            return false;
        }

        return ForbiddenNamespaces.Any(ns => trimmed.Contains(ns + ".", StringComparison.Ordinal) || trimmed.Contains(ns + ";", StringComparison.Ordinal));
    }

    private static IEnumerable<Type> Flatten(Type type)
    {
        yield return type;
        if (type.IsArray)
        {
            foreach (var inner in Flatten(type.GetElementType()!))
            {
                yield return inner;
            }
        }

        if (type.IsGenericType)
        {
            foreach (var inner in type.GetGenericArguments().SelectMany(Flatten))
            {
                yield return inner;
            }
        }
    }

    private static bool IsDomainOrApplicationType(Type type) =>
        type.Namespace is not null
        && (type.Namespace.StartsWith(Domain, StringComparison.Ordinal) || type.Namespace.StartsWith(Application, StringComparison.Ordinal));

    [GeneratedRegex("\"/?api/")]
    private static partial Regex RouteLiteral();
}
