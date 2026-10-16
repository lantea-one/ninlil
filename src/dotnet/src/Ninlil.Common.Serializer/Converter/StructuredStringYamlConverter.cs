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
    ///     The deserialized <see cref="StructuredStringModel" /> object, or <see langword="null" /> for an
    ///     empty, blank or plain null scalar.
    /// </returns>
    /// <exception cref="YamlException">The node is not a scalar, or the text does not parse.</exception>
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // YamlDotNet calls the converter with Current on this value's node.
        if (parser.Current is not Scalar scalar)
        {
            // A mapping or sequence is not a structured string. The exception is thrown before the
            // parser advances; the deserializer aborts, so the parser position no longer matters.
            Mark start = parser.Current?.Start ?? Mark.Empty;
            Mark end = parser.Current?.End ?? Mark.Empty;
            throw new YamlException(
                start,
                end,
                $"A {parser.Current?.GetType().Name} is not a valid {typeof(TObject).Name}; expected a scalar."
            );
        }

        // Advance the parser past this node so the caller continues from the next node.
        parser.MoveNext();

        // Read the text once; the trimmed form is what the model parses.
        string value = scalar.Value?.Trim();

        // An empty or blank scalar means "no value". A key with nothing after it arrives here as
        // an empty plain scalar.
        if (value is null or { Length: 0 })
            return null;

        // The YAML null words count only when plain: a quoted 'null' is text, not a null.
        if (scalar.Style == ScalarStyle.Plain && value is "~" or "null" or "Null" or "NULL")
            return null;

        // Text that does not parse, or parses to an empty model, is refused instead of read as
        // null: a silent null would drop what the caller wrote.
        if (StructuredStringModel.TryParse(value, out TObject result) && result?.IsNull() is false)
            return result;

        throw new YamlException(
            scalar.Start,
            scalar.End,
            $"'{value}' is not a valid {typeof(TObject).Name}."
        );
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
