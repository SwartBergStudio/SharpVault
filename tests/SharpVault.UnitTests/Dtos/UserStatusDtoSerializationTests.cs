// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using SharpVault.Dtos.Users;

namespace SharpVault.UnitTests.Dtos;

public sealed class UserStatusDtoSerializationTests
{
    [Theory]
    [InlineData(UserStatusDto.Active, "\"Active\"")]
    [InlineData(UserStatusDto.Locked, "\"Locked\"")]
    public void Serialize_Active_WritesTheNameNotANumber(UserStatusDto status, string expected)
    {
        Assert.Equal(expected, JsonSerializer.Serialize(status));
        Assert.Equal(expected, JsonSerializer.Serialize(status, JsonSerializerOptions.Web));
    }

    [Theory]
    [InlineData("\"Deleted\"")]
    [InlineData("\"\"")]
    [InlineData("\"Active, Locked\"")]
    [InlineData("null")]
    [Trait("Category", "Security")]
    public void Deserialize_UnknownText_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserStatusDto>(json));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("\"1\"")]
    public void Deserialize_NumericValue_ThrowsJsonException(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UserStatusDto>(json));
    }

    [Fact]
    public void Deserialize_KnownNames_RoundTrips()
    {
        Assert.Equal(UserStatusDto.Active, JsonSerializer.Deserialize<UserStatusDto>("\"Active\""));
        Assert.Equal(UserStatusDto.Locked, JsonSerializer.Deserialize<UserStatusDto>("\"Locked\""));
    }
}
