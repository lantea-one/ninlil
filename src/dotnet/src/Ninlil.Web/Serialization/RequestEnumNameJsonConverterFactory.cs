using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Converter;
using Ninlil.Common.Extension.Type;

// Define our namespace.
namespace Ninlil.Web.Serialization;

/// <summary>
///     This factory makes MVC read every request-body enum by declared name only. The type-level
///     converters (<see cref="EnumDescriptionJsonConverter{TEnum}" /> and the built-in
///     <see cref="JsonStringEnumConverter" />) also accept digit strings, and the built-in one
///     accepts JSON numbers and undefined values, so a caller could store a zone type that does
///     not exist. <c>Program.cs</c> adds the factory to the MVC <c>JsonOptions</c> only.
///     <para>
///         An options-level converter takes precedence over a <c>[JsonConverter]</c> attribute
///         on the type, which is why registering the factory there is enough. It is not in the
///         shared converters or the library serializer because cluster sync payloads, state files
///         and .NET readers of API responses also use them, and those must keep reading unnamed
///         wire codes. Responses are unchanged: <c>Write</c> goes through the converter the
///         type's own attribute names, so the OpenAPI enum lists stay the same.
///     </para>
///     <para>
///         <see cref="CanConvert" /> is true for an enum whose attribute names one of those string
///         converters. Any other enum keeps its default handling.
///     </para>
/// </summary>
public sealed class RequestEnumNameJsonConverterFactory : JsonConverterFactory
{
    /// <summary>
    ///     Returns whether <paramref name="typeToConvert" /> is an enum whose
    ///     <c>[JsonConverter]</c> attribute names <see cref="EnumDescriptionJsonConverter{TEnum}" />,
    ///     <see cref="JsonStringEnumConverter" /> or <see cref="JsonStringEnumConverter{TEnum}" />
    ///     (or a subclass of one of them).
    /// </summary>
    /// <param name="typeToConvert">The type System.Text.Json is about to convert.</param>
    /// <returns><see langword="true" /> when the factory handles the type.</returns>
    public override bool CanConvert(System.Type typeToConvert)
    {
        if (!typeToConvert.IsEnum)
            return false;

        for (
            System.Type current = AttributeConverters.GetConverterType(typeToConvert);
            current is not null;
            current = current.BaseType
        )
        {
            System.Type definition = current.IsGenericType
                ? current.GetGenericTypeDefinition()
                : current;
            if (
                definition == typeof(EnumDescriptionJsonConverter<>)
                || definition == typeof(JsonStringEnumConverter)
                || definition == typeof(JsonStringEnumConverter<>)
            )
                return true;
        }

        return false;
    }

    /// <summary>Creates the names-only converter for <paramref name="typeToConvert" />.</summary>
    /// <param name="typeToConvert">An enum type for which <see cref="CanConvert" /> is true.</param>
    /// <param name="options">The serializer options, passed on to the type's own converter.</param>
    /// <returns>A <see cref="RequestEnumNameJsonConverter{TEnum}" /> for the type.</returns>
    public override JsonConverter CreateConverter(
        System.Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        (JsonConverter)
            Activator.CreateInstance(
                typeof(RequestEnumNameJsonConverter<>).MakeGenericType(typeToConvert),
                (JsonConverter)AttributeConverters.Create(typeToConvert, options)
            );
}

/// <summary>
///     This converter reads one request-body enum value by declared name only and writes it through
///     the converter the type's own <c>[JsonConverter]</c> attribute names.
///     <see cref="RequestEnumNameJsonConverterFactory" /> creates it. A declared name is a member
///     name or a name from <c>JsonStringEnumMemberName</c>, <c>EnumMember</c> or
///     <c>DisplayName</c> (for example <c>RSASHA256</c>), matched without regard to case and
///     after trimming. A <c>[Flags]</c> type takes a comma list of such names. Digit strings, JSON
///     numbers, undefined values and a comma list on a non-flags type throw
///     <see cref="JsonException" />, which MVC reports as a 400 naming the field.
/// </summary>
/// <typeparam name="TEnum">The enum type.</typeparam>
public sealed class RequestEnumNameJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly JsonConverter<TEnum> writer;

    /// <summary>Creates the converter.</summary>
    /// <param name="writer">
    ///     The converter built from the type's own attribute. <see cref="Write" /> delegates to it.
    /// </param>
    public RequestEnumNameJsonConverter(JsonConverter<TEnum> writer) => this.writer = writer;

    /// <summary>
    ///     The names <see cref="Write" /> emits for each defined member, joined with commas. A
    ///     400 message lists them.
    /// </summary>
    public static string ValidNames => RequestEnumNames<TEnum>.ValidNames;

    /// <summary>Reads the enum from a JSON string holding declared names only.</summary>
    /// <param name="reader">The JSON reader.</param>
    /// <param name="typeToConvert">The type to convert.</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The member, or the combination of members for a flags list.</returns>
    /// <exception cref="JsonException">The token is not a string or the text names no member.</exception>
    public override TEnum Read(
        ref Utf8JsonReader reader,
        System.Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Only a string can carry a declared name. A number is quoted back as sent, so the caller
        // sees what was refused.
        if (reader.TokenType is not JsonTokenType.String)
        {
            if (reader.TokenType is JsonTokenType.Null)
                throw new JsonException(
                    $"A value is required for {typeof(TEnum).Name}. Valid values: {ValidNames}."
                );

            string token =
                reader.TokenType is JsonTokenType.Number
                    ? System.Text.Encoding.UTF8.GetString(reader.ValueSpan)
                    : reader.TokenType.ToString();
            throw new JsonException(
                $"'{token}' is not a valid value for {typeof(TEnum).Name}. Valid values: {ValidNames}."
            );
        }

        string text = reader.GetString();
        if (RequestEnumNames<TEnum>.TryParse(text, out TEnum value))
            return value;

        throw new JsonException(
            $"'{text}' is not a valid value for {typeof(TEnum).Name}. Valid values: {ValidNames}."
        );
    }

    /// <summary>
    ///     Writes through the converter the type's attribute names. A separate writer keeps the
    ///     response form, including digits for an unnamed code, exactly as it is without this
    ///     converter.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
        this.writer.Write(writer, value, options);
}

/// <summary>
///     This helper finds and builds the converter an enum type's own <c>[JsonConverter]</c>
///     attribute names. <see cref="RequestEnumNameJsonConverterFactory" /> uses it to decide which
///     types it handles, and <see cref="RequestEnumNameJsonConverter{TEnum}" /> uses the result as
///     its writer.
/// </summary>
internal static class AttributeConverters
{
    /// <summary>Finds the converter type the type's attribute names, or null when there is none.</summary>
    /// <param name="type">The type to inspect.</param>
    /// <returns>The converter type, or <see langword="null" />.</returns>
    internal static System.Type GetConverterType(System.Type type)
    {
        JsonConverterAttribute attribute = type.GetCustomAttribute<JsonConverterAttribute>(false);
        if (attribute is null)
            return null;

        // The generic attribute form, [JsonConverter<TConverter>], carries the converter type as
        // its type argument and leaves ConverterType null.
        if (attribute.ConverterType is not null)
            return attribute.ConverterType;

        System.Type attributeType = attribute.GetType();
        return attributeType.IsGenericType ? attributeType.GetGenericArguments()[0] : null;
    }

    /// <summary>Builds the converter the type's own attribute names, as System.Text.Json would.</summary>
    /// <param name="type">The enum type.</param>
    /// <param name="options">The options a converter factory receives.</param>
    /// <returns>A <c>JsonConverter&lt;T&gt;</c> for <paramref name="type" />.</returns>
    internal static JsonConverter Create(System.Type type, JsonSerializerOptions options)
    {
        var converter = (JsonConverter)Activator.CreateInstance(GetConverterType(type));
        return converter is JsonConverterFactory factory
            ? factory.CreateConverter(type, options)
            : converter;
    }
}
