// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Reflection;
using System.Runtime.CompilerServices;
using SharpVault.UnitTests.Support;

namespace SharpVault.UnitTests.Architecture;

public sealed class DtoContractTests
{
    private const string DtosAssemblyName = "SharpVault.Dtos";
    private const string DtoSuffix = "Dto";
    private const string UserStatusEnumName = "UserStatusDto";
    private const string FailClosedMemberName = "Locked";
    private const string ApiRoutesTypeName = "SharpVault.Dtos.ApiRoutes";
    private const string ContinuationTokenName = "ContinuationToken";
    private const string StrictEnumConverterName = "SharpVault.Dtos.Serialization.StrictEnumJsonConverter`1";
    private const string VaultNamespace = "SharpVault.Dtos.Vault";
    private const string SharingNamespace = "SharpVault.Dtos.Sharing";
    private const string IsExternalInitName = "System.Runtime.CompilerServices.IsExternalInit";

    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] ServerControlledFields =
        ["OwnerId", "Id", "Revision", "CreatedAt", "ModifiedAt", "DeletedAt", "DeletionBatchId", "ConcurrencyToken"];

    private static readonly string[] CredentialWords = ["Secret", "Password", "Key", "Credential", "Etag"];

    private static readonly Type[] AllowedCollections =
    [
        typeof(IReadOnlyList<>),
        typeof(IReadOnlyCollection<>),
        typeof(IEnumerable<>),
        typeof(List<>),
        typeof(IList<>),
        typeof(ICollection<>),
        typeof(Dictionary<,>),
    ];

    private static readonly string[] ProductionAssemblyNames =
    [
        "SharpVault.Domain",
        "SharpVault.Application",
        "SharpVault.Infrastructure",
        "SharpVault.Authentication",
        "SharpVault.ServiceDefaults",
        "SharpVault.Api",
        "SharpVault.UI.Shared",
        "SharpVault.PasswordGeneration",
        "SharpVault.Web",
    ];

    [Fact]
    public void DtosProjectFile_PackageAndProjectReferences_AreEmpty()
    {
        var projectFile = Path.Combine(RepositoryRoot.Path, "src", DtosAssemblyName, DtosAssemblyName + ".csproj");

        var document = System.Xml.Linq.XDocument.Load(projectFile);

        Assert.Empty(document.Descendants("PackageReference"));
        Assert.Empty(document.Descendants("ProjectReference"));
    }

    [Fact]
    public void DtosAssembly_ReferencedAssemblies_AreOnlyBaseClassLibrary()
    {
        var offenders = LoadDtos().GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System.", StringComparison.Ordinal)
                && name != "System"
                && name != "netstandard"
                && name != "mscorlib")
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void DtoTypes_PublicTypes_AreEnumsConstantsClassesOrSealedPropertyBags()
    {
        var publicTypes = PublicTypes();

        var offenders = publicTypes
            .Where(type => !(type.IsEnum || IsConstantsClass(type) || IsSealedPropertyBag(type) || IsStrictEnumConverter(type)))
            .Select(type => type.FullName)
            .ToList();

        Assert.NotEmpty(publicTypes);
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DtoClasses_Members_AreAutoPropertiesWithPublicGetAndSetOnly()
    {
        var bags = PropertyBags();

        var offenders = bags.SelectMany(MemberViolations).ToList();

        Assert.NotEmpty(bags);
        Assert.Empty(offenders);
    }

    [Fact]
    public void DtoConstantsClasses_Members_AreOnlyConstStringFields()
    {
        var constants = PublicTypes().Where(IsConstantsClass).ToList();

        var offenders = constants
            .SelectMany(type => type.GetMembers(AllDeclared)
                .Where(member => member is not FieldInfo { IsLiteral: true, FieldType: var fieldType } || fieldType != typeof(string))
                .Select(member => type.FullName + " " + member.MemberType + " " + member.Name))
            .ToList();

        Assert.NotEmpty(constants);
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DtoClasses_Properties_UseOnlyPrimitivesDtosEnumsAndCollectionsOfThose()
    {
        var dtos = LoadDtos();

        var offenders = PropertyBags()
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(property => !IsAllowedPropertyType(property.PropertyType, dtos))
                .Select(property => type.FullName + "." + property.Name + " : " + property.PropertyType.FullName))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DtoEnums_ZeroValue_IsTheFailClosedMember()
    {
        var statusEnum = LoadDtos().GetTypes().Single(type => type.Name == UserStatusEnumName);

        var zeroName = Enum.GetName(statusEnum, Enum.ToObject(statusEnum, 0));

        Assert.Equal(FailClosedMemberName, zeroName);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void DtoPropertyNames_Always_ContainNoCredentialWords()
    {
        var bags = PropertyBags();

        var offenders = bags
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.Name != ContinuationTokenName)
                .Where(property => CredentialWords.Any(word => property.Name.Contains(word, StringComparison.OrdinalIgnoreCase)))
                .Select(property => type.FullName + "." + property.Name))
            .ToList();

        Assert.NotEmpty(bags);
        Assert.Empty(offenders);
    }

    [Fact]
    public void DtoTypeNames_EndingInDto_ExistOnlyInTheDtosAssembly()
    {
        var offenders = ProductionAssemblyNames
            .SelectMany(LoadTypesOrEmpty)
            .Where(type => type.Name.EndsWith(DtoSuffix, StringComparison.Ordinal))
            .Select(type => type.FullName)
            .ToList();

        Assert.Contains(LoadDtos().GetTypes(), type => type.Name.EndsWith(DtoSuffix, StringComparison.Ordinal));
        Assert.Empty(offenders);
    }

    [Fact]
    public void DtosAssembly_StrictEnumJsonConverter_IsTheOnlyTypeWithLogic()
    {
        var logicTypes = PublicTypes()
            .Where(type => !type.IsEnum && !IsConstantsClass(type))
            .Where(type => MemberViolations(type).Any())
            .Select(type => type.FullName)
            .ToList();

        Assert.Equal([StrictEnumConverterName], logicTypes);
    }

    [Fact]
    public void ApiRoutes_Constants_AreStringConstantsOnly()
    {
        var routes = LoadDtos().GetType(ApiRoutesTypeName);

        Assert.NotNull(routes);
        Assert.True(IsConstantsClass(routes));
        var fields = routes.GetFields(AllDeclared);
        Assert.NotEmpty(fields);
        Assert.All(fields, field =>
        {
            Assert.True(field.IsLiteral, field.Name);
            Assert.Equal(typeof(string), field.FieldType);
        });
        Assert.Empty(routes.GetProperties(AllDeclared));
        Assert.Empty(routes.GetMethods(AllDeclared));
    }

    [Fact]
    public void DtoEnums_Vault_HaveNoZeroMember()
    {
        var enums = VaultEnums();

        var offenders = enums
            .Where(enumType => Enum.GetValues(enumType).Cast<object>().Any(value => Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) == 0))
            .Select(enumType => enumType.FullName)
            .ToList();

        Assert.Equal(5, enums.Count);
        Assert.Empty(offenders);
    }

    [Fact]
    public void DtoEnums_Every_UseTheStrictConverter()
    {
        var enums = PublicTypes().Where(type => type.IsEnum).ToList();

        var offenders = enums
            .Where(enumType => enumType.GetCustomAttribute<System.Text.Json.Serialization.JsonConverterAttribute>()?.ConverterType is not { IsGenericType: true } converter
                || converter.GetGenericTypeDefinition().FullName != StrictEnumConverterName
                || converter.GetGenericArguments()[0] != enumType)
            .Select(enumType => enumType.FullName)
            .ToList();

        Assert.NotEmpty(enums);
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void VaultInputDtos_Properties_ContainNoServerControlledFields()
    {
        var inputs = VaultInputDtos();

        var offenders = inputs
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => ServerControlledFields.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                .Select(property => type.FullName + "." + property.Name))
            .ToList();

        Assert.Equal(8, inputs.Count);
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void VaultInputDtos_Always_DisallowUnmappedMembers()
    {
        var inputs = VaultInputDtos();

        var offenders = inputs
            .Where(type => type.GetCustomAttribute<System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute>()?.UnmappedMemberHandling
                != System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)
            .Select(type => type.FullName)
            .ToList();

        Assert.Equal(8, inputs.Count);
        Assert.Empty(offenders);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void SharingInputDtos_Always_DisallowUnmappedMembers()
    {
        var inputs = PublicTypes()
            .Where(type => type.IsClass && type.Namespace == SharingNamespace && type.Name.EndsWith("RequestDto", StringComparison.Ordinal))
            .ToList();

        var offenders = inputs
            .Where(type => type.GetCustomAttribute<System.Text.Json.Serialization.JsonUnmappedMemberHandlingAttribute>()?.UnmappedMemberHandling
                != System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)
            .Select(type => type.FullName)
            .ToList();

        Assert.Equal(3, inputs.Count);
        Assert.Empty(offenders);
    }

    private static List<Type> VaultEnums() =>
        PublicTypes().Where(type => type.IsEnum && type.Namespace == VaultNamespace).ToList();

    private static List<Type> VaultInputDtos() =>
        PublicTypes().Where(type => type.IsClass && type.Namespace == VaultNamespace && type.Name.EndsWith("RequestDto", StringComparison.Ordinal)).ToList();

    private static Assembly LoadDtos() => Assembly.Load(new AssemblyName(DtosAssemblyName));

    private static Type[] LoadTypesOrEmpty(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name)).GetTypes();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private static List<Type> PublicTypes() =>
        LoadDtos().GetTypes()
            .Where(type => type.IsPublic || type.IsNestedPublic)
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .ToList();

    private static List<Type> PropertyBags() =>
        PublicTypes().Where(type => IsSealedPropertyBag(type) && !IsStrictEnumConverter(type)).ToList();

    private static bool IsStrictEnumConverter(Type type) => type.FullName == StrictEnumConverterName;

    private static bool IsConstantsClass(Type type) => type.IsClass && type.IsAbstract && type.IsSealed;

    private static bool IsSealedPropertyBag(Type type) => type.IsClass && type.IsSealed && !type.IsAbstract;

    private static IEnumerable<string> MemberViolations(Type type)
    {
        foreach (var field in type.GetFields(AllDeclared).Where(field => field.GetCustomAttribute<CompilerGeneratedAttribute>() is null))
        {
            yield return type.FullName + " field " + field.Name;
        }

        foreach (var method in type.GetMethods(AllDeclared).Where(method => !method.IsSpecialName))
        {
            yield return type.FullName + " method " + method.Name;
        }

        foreach (var constructor in type.GetConstructors(AllDeclared).Where(constructor => constructor.GetParameters().Length > 0 || constructor.IsStatic))
        {
            yield return type.FullName + " constructor";
        }

        foreach (var declaredEvent in type.GetEvents(AllDeclared))
        {
            yield return type.FullName + " event " + declaredEvent.Name;
        }

        foreach (var nested in type.GetNestedTypes(AllDeclared))
        {
            yield return type.FullName + " nested " + nested.Name;
        }

        foreach (var property in type.GetProperties(AllDeclared).Where(property => !IsPublicInstanceGetSet(property) || !IsAutoImplemented(property)))
        {
            yield return type.FullName + " property " + property.Name;
        }
    }

    private static bool IsAutoImplemented(PropertyInfo property) =>
        property.GetMethod?.GetCustomAttribute<CompilerGeneratedAttribute>() is not null
        && property.SetMethod?.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    private static bool IsPublicInstanceGetSet(PropertyInfo property)
    {
        var getter = property.GetMethod;
        var setter = property.SetMethod;
        if (getter is not { IsPublic: true, IsStatic: false } || setter is not { IsPublic: true, IsStatic: false })
        {
            return false;
        }

        return !setter.ReturnParameter.GetRequiredCustomModifiers().Any(modifier => modifier.FullName == IsExternalInitName);
    }

    private static bool IsAllowedPropertyType(Type type, Assembly dtos)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsPrimitive
            || underlying == typeof(string)
            || underlying == typeof(Guid)
            || underlying == typeof(DateTimeOffset)
            || underlying == typeof(DateTime)
            || underlying == typeof(decimal)
            || underlying == typeof(TimeSpan)
            || underlying.Assembly == dtos)
        {
            return true;
        }

        if (underlying.IsArray)
        {
            return IsAllowedPropertyType(underlying.GetElementType()!, dtos);
        }

        return underlying.IsGenericType
            && AllowedCollections.Contains(underlying.GetGenericTypeDefinition())
            && underlying.GetGenericArguments().All(argument => IsAllowedPropertyType(argument, dtos));
    }
}
