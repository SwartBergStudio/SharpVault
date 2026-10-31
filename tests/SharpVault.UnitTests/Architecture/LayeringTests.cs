// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using SharpVault.Infrastructure.Encryption;
using SharpVault.Infrastructure.Encryption.Development;
using SharpVault.Infrastructure.Encryption.KeyVault;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed class LayeringTests
{
    private const string Domain = "SharpVault.Domain";
    private const string Application = "SharpVault.Application";
    private const string Infrastructure = "SharpVault.Infrastructure";
    private const string ServiceDefaults = "SharpVault.ServiceDefaults";
    private const string Api = "SharpVault.Api";
    private const string UiShared = "SharpVault.UI.Shared";
    private const string Web = "SharpVault.Web";

    private const string AuthenticationHandlerName = "Microsoft.AspNetCore.Authentication.AuthenticationHandler`1";
    private const string RazorImportsTypeName = "_Imports";
    private const string ComponentBaseName = "Microsoft.AspNetCore.Components.ComponentBase";

    public static TheoryData<string> ProductionAssemblyNames { get; } =
        [Domain, Application, Infrastructure, ServiceDefaults, Api, UiShared, Web];

    [Fact]
    public void Domain_ReferencedAssemblies_ContainNoOtherSharpVaultProject()
    {
        var referenced = ReferencedNames(Domain);

        Assert.DoesNotContain(referenced, name => name.StartsWith("SharpVault.", StringComparison.Ordinal));
    }

    [Fact]
    public void Domain_ReferencedAssemblies_ContainNoStorageWebOrApplicationFramework()
    {
        var referenced = ReferencedNames(Domain);

        Assert.DoesNotContain(referenced, name => name.StartsWith("Azure.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Mapster", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("FluentValidation", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name == "Mediator" || name.StartsWith("Mediator.", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_ReferencedAssemblies_ContainOnlyDomainAmongSharpVaultProjects()
    {
        var sharpVaultReferences = ReferencedNames(Application)
            .Where(name => name.StartsWith("SharpVault.", StringComparison.Ordinal))
            .ToList();

        Assert.All(sharpVaultReferences, name => Assert.Equal(Domain, name));
    }

    [Fact]
    public void Application_ReferencedAssemblies_ContainNoAzureSdkOrAspNetCore()
    {
        var referenced = ReferencedNames(Application);

        Assert.DoesNotContain(referenced, name => name.StartsWith("Azure.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_ReferencedAssemblies_ContainNoApiWebOrUiProject()
    {
        var referenced = ReferencedNames(Infrastructure);

        Assert.DoesNotContain(Api, referenced);
        Assert.DoesNotContain(Web, referenced);
        Assert.DoesNotContain(UiShared, referenced);
    }

    [Fact]
    public void UiShared_ReferencedAssemblies_ContainNoServerSideProjectOrServerFramework()
    {
        var referenced = ReferencedNames(UiShared);

        Assert.DoesNotContain(Infrastructure, referenced);
        Assert.DoesNotContain(Api, referenced);
        Assert.DoesNotContain(Web, referenced);
        Assert.DoesNotContain(referenced, name => name.StartsWith("Azure.", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore.Http", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore.Hosting", StringComparison.Ordinal));
        Assert.DoesNotContain(referenced, name => name.StartsWith("Microsoft.AspNetCore.Mvc", StringComparison.Ordinal));
    }

    [Fact]
    public void ServiceDefaults_ReferencedAssemblies_ContainNoOtherSharpVaultProject()
    {
        var referenced = ReferencedNames(ServiceDefaults);

        Assert.DoesNotContain(referenced, name => name.StartsWith("SharpVault.", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_ReferencedAssemblies_ContainNoWebOrUiProject()
    {
        var referenced = ReferencedNames(Api);

        Assert.DoesNotContain(Web, referenced);
        Assert.DoesNotContain(UiShared, referenced);
    }

    [Fact]
    public void Web_ReferencedAssemblies_ContainNoApiInfrastructureApplicationOrDomainProject()
    {
        var referenced = ReferencedNames(Web);

        Assert.DoesNotContain(Api, referenced);
        Assert.DoesNotContain(Infrastructure, referenced);
        Assert.DoesNotContain(Application, referenced);
        Assert.DoesNotContain(Domain, referenced);
    }

    [Theory]
    [InlineData(Domain, new string[0])]
    [InlineData(Application, new[] { Domain })]
    [InlineData(Infrastructure, new[] { Application })]
    [InlineData(ServiceDefaults, new string[0])]
    [InlineData(Api, new[] { Application, Infrastructure, ServiceDefaults })]
    [InlineData(UiShared, new string[0])]
    [InlineData(Web, new[] { UiShared, ServiceDefaults })]
    public void ProjectFiles_ProjectReferences_MatchTheAllowedLayering(string project, string[] allowed)
    {
        var projectFile = Path.Combine(RepositoryRoot.Path, "src", project, project + ".csproj");

        var references = System.Xml.Linq.XDocument.Load(projectFile)
            .Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string?)element.Attribute("Include") ?? string.Empty))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(allowed.Order(StringComparer.Ordinal).ToArray(), references);
    }

    [Fact]
    public void DomainProjectFile_PackageReferences_AreEmpty()
    {
        var projectFile = Path.Combine(RepositoryRoot.Path, "src", Domain, Domain + ".csproj");

        var packages = System.Xml.Linq.XDocument.Load(projectFile).Descendants("PackageReference");

        Assert.Empty(packages);
    }

    [Fact]
    public void UiSharedProjectFile_FrameworkReferences_AreEmpty()
    {
        var projectFile = Path.Combine(RepositoryRoot.Path, "src", UiShared, UiShared + ".csproj");

        var frameworks = System.Xml.Linq.XDocument.Load(projectFile).Descendants("FrameworkReference");

        Assert.Empty(frameworks);
    }

    [Fact]
    public void ProductionAssemblies_ConcreteClasses_AreSealed()
    {
        var unsealed = ProductionAssemblyNamesList()
            .SelectMany(name => LoadTypes(name))
            .Where(IsInSharpVaultNamespace)
            .Where(type => type.IsClass && !type.IsAbstract && !type.IsSealed)
            .Where(type => !IsGenerated(type))
            .Where(type => !type.Name.Contains('<', StringComparison.Ordinal))
            .Where(type => !DerivesFrom(type, ComponentBaseName))
            .Where(type => !IsRazorImportsMarker(type))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(unsealed);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ProductionAssemblies_Types_DeclareNoAuthenticationHandler()
    {
        var handlers = ProductionAssemblyNamesList()
            .SelectMany(name => LoadTypes(name))
            .Where(type => DerivesFromGeneric(type, AuthenticationHandlerName))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(handlers);
    }

    [Fact]
    public void ProductionAssemblies_Namespaces_StartWithAssemblyName()
    {
        var misplaced = ProductionAssemblyNamesList()
            .SelectMany(name => LoadTypes(name).Select(type => (Assembly: name, Type: type)))
            .Where(item => IsInSharpVaultNamespace(item.Type))
            .Where(item => !item.Type.Namespace!.StartsWith(item.Assembly, StringComparison.Ordinal))
            .Select(item => item.Type.FullName)
            .ToList();

        Assert.Empty(misplaced);
    }

    [Theory]
    [MemberData(nameof(ProductionAssemblyNames))]
    public void ProductionAssemblies_Assembly_CanBeLoadedByName(string assemblyName)
    {
        var assembly = Assembly.Load(new AssemblyName(assemblyName));

        Assert.Equal(assemblyName, assembly.GetName().Name);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ProductionAssemblies_ReferencedAssemblies_ContainNoTestingPackages()
    {
        var offenders = ProductionAssemblyNamesList()
            .SelectMany(name => ReferencedNames(name).Select(reference => (Assembly: name, Reference: reference)))
            .Where(item => item.Reference.StartsWith("xunit", StringComparison.OrdinalIgnoreCase)
                || item.Reference.StartsWith("NSubstitute", StringComparison.Ordinal)
                || item.Reference.StartsWith("AutoFixture", StringComparison.Ordinal)
                || item.Reference.StartsWith("Testcontainers", StringComparison.Ordinal)
                || item.Reference.StartsWith("Microsoft.AspNetCore.TestHost", StringComparison.Ordinal)
                || item.Reference.StartsWith("SharpVault.UnitTests", StringComparison.Ordinal)
                || item.Reference.StartsWith("SharpVault.IntegrationTests", StringComparison.Ordinal))
            .Select(item => item.Assembly + " -> " + item.Reference)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ProductionAssemblies_KeyWrapProviders_AreOnlyKeyVaultAndDevelopment()
    {
        var implementations = ProductionAssemblyNamesList()
            .SelectMany(name => LoadTypes(name))
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IKeyWrapProvider).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var expected = new[] { typeof(DevelopmentKeyWrapProvider).FullName, typeof(KeyVaultKeyWrapProvider).FullName }
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, implementations);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void ProductionAssemblies_KeyWrapProviderInterface_IsImplementedByNoOtherInterface()
    {
        var derivedInterfaces = ProductionAssemblyNamesList()
            .SelectMany(name => LoadTypes(name))
            .Where(type => type.IsInterface && type != typeof(IKeyWrapProvider) && typeof(IKeyWrapProvider).IsAssignableFrom(type))
            .Select(type => type.FullName)
            .ToList();

        Assert.Empty(derivedInterfaces);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SourceFiles_Src_DoNotUseSystemRandom()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepositoryRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !path.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(path =>
            {
                var content = File.ReadAllText(path);
                return content.Contains("new Random(", StringComparison.Ordinal)
                    || content.Contains("Random.Shared", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(RepositoryRoot.Path, path))
            .ToList();

        Assert.Empty(offenders);
    }

    private static string[] ProductionAssemblyNamesList() => [Domain, Application, Infrastructure, ServiceDefaults, Api, UiShared, Web];

    private static List<string> ReferencedNames(string assemblyName) =>
        Assembly.Load(new AssemblyName(assemblyName))
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToList();

    private static Type[] LoadTypes(string assemblyName) =>
        Assembly.Load(new AssemblyName(assemblyName)).GetTypes();

    private static bool IsInSharpVaultNamespace(Type type) =>
        type.Namespace is not null
        && (type.Namespace == "SharpVault" || type.Namespace.StartsWith("SharpVault.", StringComparison.Ordinal));

    private static bool IsGenerated(Type type) =>
        type.GetCustomAttributesData().Any(attribute =>
            attribute.AttributeType.FullName is "System.Runtime.CompilerServices.CompilerGeneratedAttribute"
                or "System.CodeDom.Compiler.GeneratedCodeAttribute");

    private static bool IsRazorImportsMarker(Type type) =>
        type.Name == RazorImportsTypeName
        && type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .All(member => member is ConstructorInfo || member is MethodInfo { Name: "Execute" });

    private static bool DerivesFrom(Type type, string baseFullName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.FullName == baseFullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool DerivesFromGeneric(Type type, string genericDefinitionName)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().FullName == genericDefinitionName)
            {
                return true;
            }
        }

        return false;
    }
}
