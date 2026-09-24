using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Converter;

/// <summary>
///     This class maintains the JSON converter for converting <see cref="System.Enum" /> values to and from JSON.
/// </summary>
public class EnumDescriptionJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : System.Enum
{
    /// <summary>
    ///     This method reads the <typeparamref name="TEnum" /> value
    ///     from a JSON string in the <paramref name="reader" />.
    /// </summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The <typeparamref name="TEnum" /> value.</returns>
    public override TEnum Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        reader
            .GetString()
            .ToEnum<TEnum>(
                attribute =>
                    attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null,
                attribute =>
                    attribute is EnumMemberAttribute a
                    && a.Value?.Trim() is not (null or { Length: 0 })
                        ? a.Value.Trim()
                        : null,
                attribute => attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null
            );

    /// <summary>
    ///     This method writes a <typeparamref name="TEnum" /> value to a JSON string in the <paramref name="writer" />.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The <typeparamref name="TEnum" /> value to write.</param>
    /// <param name="options">The JSON serializer options.</param>
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        writer.WriteStringValue(
            value.ToDescription(
                attribute =>
                    attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null,
                attribute =>
                    attribute is EnumMemberAttribute a
                    && a.Value?.Trim() is not (null or { Length: 0 })
                        ? a.Value.Trim()
                        : null,
                attribute => attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null
            )
        );
}
