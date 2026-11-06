// SharpVault - Copyright (c) SwartBergStudio.
// Licensed under the GNU Affero General Public License v3.0. See LICENSE in the project root.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpVault.Dtos.Serialization;

public sealed class StrictEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException();
        }

        var text = reader.GetString();
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (string.Equals(name, text, StringComparison.Ordinal))
            {
                return Enum.Parse<TEnum>(name);
            }
        }

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        var name = Enum.GetName(value);
        if (name is null)
        {
            throw new JsonException();
        }

        writer.WriteStringValue(name);
    }
}
