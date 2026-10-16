using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.String;

// Define our namespace.
namespace Ninlil.Common.Extension.Converter;

/// <summary>
///     This System.Text.Json converter writes an enum value as its description string and reads it
///     back. The name comes from, in order, <see cref="JsonStringEnumMemberNameAttribute" />,
///     <see cref="EnumMemberAttribute" /> and <see cref="DisplayNameAttribute" />, falling back to
///     the member name (see <see cref="ToDescriptionEnumExtension" />). Ninlil's API enums use it so their JSON
///     values are the names declared on each member (such as <c>IN</c> for a DNS class).
///     <para>
///         <c>Read</c> refuses text that names no member, a blank string, <c>null</c> and any token
///         that is not a string by throwing <see cref="JsonException" />, which MVC reports as a 400
///         validation problem for the field. It never falls back to the first member. A number
///         string is accepted when it fits the underlying type (see
///         <see cref="ToEnumStringExtension" />), so a code with no member reads back. A nullable
///         property is not affected: System.Text.Json handles <c>null</c> before the converter.
///     </para>
///     <para>
///         The converter does not touch stored JSON. The library's serializer registers an
///         options-level <c>JsonStringEnumConverter</c>, which takes precedence over the
///         <c>[JsonConverter]</c> attribute on an enum type.
///     </para>
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
    /// <exception cref="JsonException">
    ///     The token is not a string, or the string matches no value of <typeparamref name="TEnum" />.
    /// </exception>
    public override TEnum Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Refuse null and every other non-string token; there is no member to read it as.
        if (reader.TokenType is not JsonTokenType.String)
            throw new JsonException(
                $"Expected a string value for {typeof(TEnum).Name} but found {reader.TokenType}."
            );

        // Resolve the text to a member, or to a number that fits the underlying type.
        string text = reader.GetString();
        return text.TryToEnum(
            out TEnum value,
            attribute => attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null,
            attribute =>
                attribute is EnumMemberAttribute a && a.Value?.Trim() is not (null or { Length: 0 })
                    ? a.Value.Trim()
                    : null,
            attribute => attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null
        )
            ? value
            : throw new JsonException($"'{text}' is not a valid value for {typeof(TEnum).Name}.");
    }

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
