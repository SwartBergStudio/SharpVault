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

    public static TheoryData<Type, object> EveryEnumMember()
    {
        var data = new TheoryData<Type, object>();
        foreach (var enumType in EnumTypes)
        {
            foreach (var value in Enum.GetValues(enumType))
            {
                data.Add(enumType, value);
            }
        }

        return data;
    }

    public static TheoryData<Type, string> InvalidEnumJson()
    {
        var data = new TheoryData<Type, string>();
        foreach (var enumType in EnumTypes)
        {
            var first = Enum.GetNames(enumType)[0];
            var second = Enum.GetNames(enumType)[1];
            foreach (var json in new[]
            {
                "1",
                "0",
                "-1",
                "\"1\"",
                "\"" + first.ToLowerInvariant() + "\"",
                "\"" + first.ToUpperInvariant() + "\"",
                "\"" + first + "," + second + "\"",
                "\"" + first + ", " + second + "\"",
                "\" " + first + "\"",
                "\"Unknown\"",
                "\"\"",
                "null",
                "true",
                "[]",
            })
            {
                data.Add(enumType, json);
            }
        }

        return data;
    }

    public static TheoryData<Type> InputDtoTypes()
    {
        var data = new TheoryData<Type>();
        foreach (var type in typeof(SecretDto).Assembly.GetTypes()
            .Where(type => type.Namespace == VaultNamespace && type.Name.EndsWith("RequestDto", StringComparison.Ordinal))
            .OrderBy(type => type.Name, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryEnumMember))]
    public void Enum_ExactName_RoundTrips(Type enumType, object value)
    {
        var json = JsonSerializer.Serialize(value, enumType);

        Assert.Equal("\"" + value + "\"", json);
        Assert.Equal(value, JsonSerializer.Deserialize(json, enumType));
        Assert.Equal(value, JsonSerializer.Deserialize(json, enumType, JsonSerializerOptions.Web));
    }

    [Theory]
    [MemberData(nameof(InvalidEnumJson))]
    [Trait("Category", "Security")]
    public void Enum_IntegerLowercaseCommaOrUnknown_ThrowsJsonException(Type enumType, string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, enumType));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Enum_UndefinedValue_ThrowsOnWrite(int number)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Serialize((SecretTypeDto)number));
    }

    [Fact]
    public void NullableEnum_Null_RoundTrips()
    {
        Assert.Equal("null", JsonSerializer.Serialize<SecretTypeDto?>(null));
        Assert.Null(JsonSerializer.Deserialize<SecretTypeDto?>("null"));
        Assert.Equal(SecretTypeDto.ApiKey, JsonSerializer.Deserialize<SecretTypeDto?>("\"ApiKey\""));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SecretTypeDto?>("\"apikey\""));
    }

    [Fact]
    public void RecycleBinItemDto_NullType_RoundTrips()
    {
        var item = new RecycleBinItemDto { Kind = RecycleBinItemKindDto.Folder, Id = Guid.NewGuid(), Type = null };

        var copy = JsonSerializer.Deserialize<RecycleBinItemDto>(JsonSerializer.Serialize(item));

        Assert.NotNull(copy);
        Assert.Null(copy.Type);
        Assert.Equal(RecycleBinItemKindDto.Folder, copy.Kind);
    }

    [Theory]
    [MemberData(nameof(InputDtoTypes))]
    [Trait("Category", "Security")]
    public void InputDto_UnknownMember_ThrowsJsonException(Type dtoType)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"ownerId\":\"" + Guid.NewGuid() + "\"}", dtoType, JsonSerializerOptions.Web));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"revision\":5}", dtoType));
    }

    [Theory]
    [MemberData(nameof(InputDtoTypes))]
    [Trait("Category", "Security")]
    public void InputDto_ToString_DoesNotContainValue(Type dtoType)
    {
        var dto = Activator.CreateInstance(dtoType)!;
        foreach (var property in dtoType.GetProperties().Where(property => property.PropertyType == typeof(string)))
        {
            property.SetValue(dto, SecretMarker);
        }

        var text = dto.ToString();

        Assert.DoesNotContain(SecretMarker, text, StringComparison.Ordinal);
        Assert.Equal(dtoType.FullName, text);
    }

    [Fact]
    public void CreateSecretRequestDto_Json_RoundTripsFields()
    {
        var request = new CreateSecretRequestDto
        {
            Type = SecretTypeDto.Login,
            Name = "n",
            Value = SecretMarker,
            Tags = ["a", "b"],
            FolderId = Guid.NewGuid(),
        };

        var copy = JsonSerializer.Deserialize<CreateSecretRequestDto>(JsonSerializer.Serialize(request, JsonSerializerOptions.Web), JsonSerializerOptions.Web);

        Assert.NotNull(copy);
        Assert.Equal(request.Type, copy.Type);
        Assert.Equal(request.Name, copy.Name);
        Assert.Equal(request.Value, copy.Value);
        Assert.Equal(request.Tags, copy.Tags);
        Assert.Equal(request.FolderId, copy.FolderId);
    }
}
