using Ninlil.Common.Serializer.Model;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This YamlDotNet type converter writes a structured string model (see
///     <see cref="IStructuredStringModel" />) as a plain scalar and parses it back with
///     <see cref="StructuredStringModel.TryParse{TModel}(string, out TModel)" />, so the model appears
///     in YAML as a string instead of a mapping. Blank or unparseable text reads as
///     <see langword="null" />.
/// </summary>
/// <typeparam name="TObject">The type of object to convert.</typeparam>
public class StructuredStringYamlConverter<TObject> : IYamlTypeConverter
    where TObject : class, IStructuredStringModel, new()
{
    /// <summary>
    ///     This method determines if the converter accepts the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is accepted or not.</returns>
    public bool Accepts(Type type) => type == typeof(TObject);

    /// <summary>
    ///     This method reads a YAML representation of an <see cref="IStructuredStringModel" /> object.
    /// </summary>
    /// <param name="parser">The YAML parser.</param>
    /// <param name="type">The type to deserialize to.</param>
    /// <param name="rootDeserializer">The root deserializer function.</param>
    /// <returns>
    ///     The deserialized <see cref="StructuredStringModel" /> object or <see langword="null" /> if parsing fails.
    /// </returns>
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // YamlDotNet calls the converter with Current on this value's node. Read it, then
        // advance so the caller continues from the next node.
        Scalar scalar = parser.Current as Scalar;

        // Advance the parser past this node.
        parser.MoveNext();

        // Ensure we have a scalar value to read. If not, return null.
        if (scalar is null)
            return null;

        // Ensure we have a value to read. If the value is null or empty, return null.
        if (scalar.Value.Trim() is null or { Length: 0 })
            return null;

        // We're done, try to parse the value into our structured string
        // model. If parsing fails or the result is null, return null.
        return
            StructuredStringModel.TryParse(scalar.Value.Trim(), out TObject result)
            && result?.IsNull() is false
            ? result
            : null;
    }

    /// <summary>
    ///     This method writes a YAML representation of an <see cref="IStructuredStringModel" /> object.
    /// </summary>
    /// <param name="emitter">The YAML emitter.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type of the <paramref name="value" />.</param>
    /// <param name="serializer">The serializer function.</param>
    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        // Ensure we have a value to write.
        if (value?.ToString()?.Trim() is null or { Length: 0 })
        {
            // Write an empty string to the YAML output.
            emitter.Emit(new Scalar(string.Empty));

            // We're done, so return from the method.
            return;
        }

        // Write the string value to the YAML output, ensuring we trim any whitespace.
        emitter.Emit(new Scalar(value!.ToString()!.Trim()));
    }
}
