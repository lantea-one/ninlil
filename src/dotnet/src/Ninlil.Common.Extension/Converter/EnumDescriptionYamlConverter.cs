using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using Ninlil.Common.Extension.Enum;
using Ninlil.Common.Extension.String;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Extension.Converter;

/// <summary>
///     This YamlDotNet type converter writes any enum value as its description string and reads
///     it back. The name comes from, in order, <see cref="YamlMemberAttribute.Alias" />,
///     <see cref="EnumMemberAttribute" />, <see cref="DisplayNameAttribute" /> and
///     <see cref="JsonStringEnumMemberNameAttribute" />, falling back to the member name. It is the
///     YAML counterpart of
///     <see cref="EnumDescriptionJsonConverter{TEnum}" />.
/// </summary>
public class EnumDescriptionYamlConverter : IYamlTypeConverter
{
    /// <summary>
    ///     This method determines if the converter accepts the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is accepted or not.</returns>
    public bool Accepts(System.Type type) => type.IsEnum;

    /// <summary>
    ///     This method reads a YAML representation of an <see cref="System.Enum" /> object.
    /// </summary>
    /// <param name="parser">The YAML parser.</param>
    /// <param name="type">The type to deserialize to.</param>
    /// <param name="rootDeserializer">The root deserializer function.</param>
    /// <returns>
    ///     The deserialized <see cref="System.Enum" /> object, or <see langword="null" /> when the
    ///     current node is not a scalar or no member matches.
    /// </returns>
    public object ReadYaml(IParser parser, System.Type type, ObjectDeserializer rootDeserializer)
    {
        // YamlDotNet calls a type converter with Current already on this value's scalar node.
        // Read it first, then advance with MoveNext(); advancing first would skip the value.
        if (parser.Current is not Scalar scalar)
            return null;

        // Localize and trim the scalar's raw text value before advancing the parser past it.
        string value = scalar.Value?.Trim();
        parser.MoveNext();

        // Try to resolve the scalar text back to an enum member, checking every supported
        // description-bearing attribute in turn, then return the resolved value or null.
        return value.TryToEnum(
            out var enumValue,
            type,
            attribute => attribute is YamlMemberAttribute a ? a.Alias?.Trim() : null,
            attribute =>
                attribute is EnumMemberAttribute a && a.Value?.Trim() is not (null or { Length: 0 })
                    ? a.Value.Trim()
                    : null,
            attribute => attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null,
            attribute => attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null
        )
            ? enumValue
            : null;
    }

    /// <summary>
    ///     This method writes an enum value as a scalar holding its description, or <c>~</c> (YAML
    ///     null) for anything that is not an enum.
    /// </summary>
    /// <param name="emitter">The YAML emitter.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type of the <paramref name="value" />.</param>
    /// <param name="serializer">The serializer function.</param>
    public void WriteYaml(
        IEmitter emitter,
        object value,
        System.Type type,
        ObjectSerializer serializer
    ) =>
        // Pass the concrete enum `type` to ToDescription. Calling the generic overload on a
        // System.Enum-typed value binds TEnum to System.Enum itself, the attribute lookup finds no
        // field, and the result falls back to the raw ToString().
        emitter.Emit(
            new Scalar(
                value is System.Enum enumValue
                    ? enumValue.ToDescription(
                        type,
                        attribute => attribute is YamlMemberAttribute a ? a.Alias?.Trim() : null,
                        attribute =>
                            attribute is EnumMemberAttribute a
                            && a.Value?.Trim() is not (null or { Length: 0 })
                                ? a.Value.Trim()
                                : null,
                        attribute =>
                            attribute is DisplayNameAttribute a ? a.DisplayName.Trim() : null,
                        attribute =>
                            attribute is JsonStringEnumMemberNameAttribute a ? a.Name.Trim() : null
                    )
                    : "~"
            )
        );
}
