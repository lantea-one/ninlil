using System.Drawing;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This YamlDotNet type converter writes a <see cref="Color" /> or nullable <see cref="Color" />
///     as a CSS <c>rgb(r, g, b)</c> string, or <c>~</c> (YAML null) for an empty color, and reads it
///     back through <see cref="RgbColorParsingHelper" />. Blank, null or unparseable text reads as
///     <see langword="null" /> for a nullable target and <see cref="Color.Empty" /> otherwise.
/// </summary>
public class RgbColorYamlConverter : IYamlTypeConverter
{
    /// <summary>
    ///     This method determines if the converter accepts the specified <paramref name="type" />.
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>A <see cref="bool" /> indicating whether the type is accepted or not.</returns>
    public bool Accepts(Type type) => type == typeof(Color) || type == typeof(Color?);

    /// <summary>
    ///     This method reads a YAML representation of an <see cref="Color" /> object.
    /// </summary>
    /// <param name="parser">The YAML parser.</param>
    /// <param name="type">The type to deserialize to.</param>
    /// <param name="rootDeserializer">The root deserializer function.</param>
    /// <returns>
    ///     The deserialized <see cref="Color" />, or the empty result described in the class summary.
    /// </returns>
    public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
    {
        // Read the current scalar's text, then advance so the caller continues from the next
        // node.
        var rgbString = (parser.Current as Scalar)?.Value?.Trim();

        // Advance the parser past this node.
        parser.MoveNext();

        // A blank or YAML-null value is "no color".
        if (rgbString?.Trim() is null or { Length: 0 } or "~")
            return type == typeof(Color?) ? null : Color.Empty;

        // Otherwise, parse the rgb(r, g, b) string into a Color, falling back to the same
        // "no color" result used above if it doesn't match the expected format (rather than
        // throwing).
        return RgbColorParsingHelper.TryParse(rgbString, out Color color) ? color
            : type == typeof(Color?) ? null
            : Color.Empty;
    }

    /// <summary>
    ///     This method writes a YAML representation of an <see cref="Color" /> object.
    /// </summary>
    /// <param name="emitter">The YAML emitter.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="type">The type of the <paramref name="value" />.</param>
    /// <param name="serializer">The serializer function.</param>
    public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
    {
        // Anything that is not a color, and Color.Empty, is written as YAML null.
        if (value is not Color color || color == Color.Empty)
        {
            // Write a null value to the YAML emitter.
            emitter.Emit(new Scalar("~"));

            // We're done, return early.
            return;
        }

        // We're done, convert the Color object to a string representation of the RGB color value.
        emitter.Emit(new Scalar($"rgb({color.R}, {color.G}, {color.B})"));
    }
}
