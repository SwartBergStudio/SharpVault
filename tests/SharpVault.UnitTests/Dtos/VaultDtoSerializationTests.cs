// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using SharpVault.Dtos.Vault;

namespace SharpVault.UnitTests.Dtos;

public sealed class VaultDtoSerializationTests
{
    private const string VaultNamespace = "SharpVault.Dtos.Vault";
    private const string SecretMarker = "marker-secret-value-9f3a";

    private static readonly Type[] EnumTypes =
        [typeof(SecretTypeDto), typeof(FolderDeleteModeDto), typeof(RecycleBinItemKindDto), typeof(RevealFieldDto), typeof(RevealIntentDto)];

    [Fact]
    public void Enum_EveryMember_SerializesToItsNameAndRoundTrips()
    {
        foreach (var enumType in EnumTypes)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                var json = JsonSerializer.Serialize(value, enumType);

                Assert.Equal("\"" + value + "\"", json);
                Assert.Equal(value, JsonSerializer.Deserialize(json, enumType));
            }
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Enum_NumberLowercaseCommaOrUnknown_ThrowsJsonException()
    {
        foreach (var enumType in EnumTypes)
        {
            var first = Enum.GetNames(enumType)[0];
            var second = Enum.GetNames(enumType)[1];
            foreach (var json in new[] { "1", "\"1\"", "\"" + first.ToLowerInvariant() + "\"", "\"" + first + "," + second + "\"", "\"Unknown\"" })
            {
                Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, enumType));
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Enum_UndefinedValue_ThrowsOnWrite(int number)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((SecretTypeDto)number));
    }

    [Fact]
    [Trait("Category", "Security")]
    public void InputDtos_UnknownMember_ThrowsJsonException()
    {
        foreach (var dtoType in InputDtoTypes())
        {
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"ownerId\":\"" + Guid.NewGuid() + "\"}", dtoType, JsonSerializerOptions.Web));
            Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"revision\":5}", dtoType));
        }
    }

    [Fact]
    [Trait("Category", "Security")]
    public void InputDtos_ToString_DoesNotContainValue()
    {
        foreach (var dtoType in InputDtoTypes())
        {
            var dto = Activator.CreateInstance(dtoType)!;
            foreach (var property in dtoType.GetProperties().Where(property => property.PropertyType == typeof(string)))
            {
                property.SetValue(dto, SecretMarker);
            }

            Assert.DoesNotContain(SecretMarker, dto.ToString(), StringComparison.Ordinal);
        }
    }

    private static IEnumerable<Type> InputDtoTypes() =>
        typeof(SecretDto).Assembly.GetTypes()
            .Where(type => type.Namespace == VaultNamespace && type.Name.EndsWith("RequestDto", StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal);
}
