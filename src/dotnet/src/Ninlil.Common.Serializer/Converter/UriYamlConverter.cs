using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This YamlDotNet type converter writes a <see cref="Uri" /> as its string form and reads it back. Unparseable
///     text reads as <see langword="null" />, and a null value is written as an empty string.
/// </summary>
public class UriYamlConverter : IYamlTypeConverter
{
    /// <summary>
    ///     This method determines if the converter accepts the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is accepted or not.</returns>
    public bool Accepts(Type type) => type == typeof(Uri);

    /// <summary>
    ///     This method reads a YAML representation of an <see cref="Uri" /> object.
    /// </summary>
    /// <param name="parser">The YAML parser.</param>
    /// <param name="type">The type to deserialize to.</param>
    /// <param name="rootDeserializer">The root deserializer function.</param>
    /// <returns>
    ///     The deserialized <see cref="Uri" /> object or <see langword="null" /> if parsing fails.
    /// </returns>
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // YamlDotNet calls the converter with Current on this value's node. Read it, then
        // advance so the caller continues from the next node.
        Scalar scalar = parser.Current as Scalar;

        // Advance the parser past this node.
        parser.MoveNext();

        // We're done, parse the captured scalar's value into a URI, or return null.
        return
            scalar is not null
            && Uri.TryCreate(scalar.Value?.Trim(), UriKind.RelativeOrAbsolute, out var address)
            ? address
            : null;
    }

    /// <summary>
    ///     This method writes a YAML representation of an <see cref="Uri" /> object.
    /// </summary>
    /// <param name="emitter">The YAML emitter.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type of the <paramref name="value" />.</param>
    /// <param name="serializer">The serializer function.</param>
    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer) =>
        emitter.Emit(new Scalar((value as Uri)?.ToString() ?? ""));
}
