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
///     This class provides a YAML type converter for <see cref="System.Enum" /> objects.
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
    ///     The deserialized <see cref="System.Enum" /> object or <see langword="null" /> if parsing fails.
    /// </returns>
    public object ReadYaml(IParser parser, System.Type type, ObjectDeserializer rootDeserializer)
    {
        // The parser's Current event is already positioned on the scalar node for this value when
        // YamlDotNet invokes a registered IYamlTypeConverter -- read it first, then advance past it
        // with MoveNext(). Calling MoveNext() before inspecting Current (as an earlier version of
        // this method did) skips past the real value node entirely.
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
    ///     This method writes a YAML representation of an <see cref="System.Enum" /> object.
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
        // Explicitly pass the real, concrete enum `type` YamlDotNet hands us to ToDescription
        // instead of calling TryToDescription off a `value as System.Enum`-typed receiver: the
        // latter's compile-time type is the abstract System.Enum base class, which resolves the
        // generic ToDescription/TryToDescription overload with TEnum bound to System.Enum itself
        // rather than the real enum type, so the underlying lookup finds no matching field/type
        // and silently falls back to the enum's raw ToString(). Passing `type` explicitly avoids
        // that trap and always resolves against the actual runtime enum type.
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
