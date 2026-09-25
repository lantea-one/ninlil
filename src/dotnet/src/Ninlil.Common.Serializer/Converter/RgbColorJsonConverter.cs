using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This System.Text.Json converter writes a <see cref="Color" /> as a CSS <c>rgb(r, g, b)</c>
///     string and reads it back through <see cref="RgbColorParsingHelper" />. <see cref="Color.Empty" />
///     means "no color": it is written as JSON null, and blank or unparseable text reads as it.
///     Alpha is dropped.
/// </summary>
public class RgbColorJsonConverter : JsonConverter<Color>
{
    /// <summary>
    ///     This method reads and converts the JSON to an <see cref="Color" />.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">The <see cref="Type" /> of object to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    /// <returns>The converted <see cref="Color" />.</returns>
    public override Color Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Localize the color string from the JSON reader.
        var rgbString = reader.GetString();

        // If the RGB string is null or empty, return an empty color to indicate "no color".
        // Otherwise, parse the rgb(r, g, b) string into a Color, falling back to an empty color
        // if it doesn't match the expected format (rather than throwing).
        return rgbString?.Trim() is null or { Length: 0 } ? Color.Empty
            : RgbColorParsingHelper.TryParse(rgbString, out Color color) ? color
            : Color.Empty;
    }

    /// <summary>
    ///     This method writes a <see cref="Color" /> as JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter" /> to write to.</param>
    /// <param name="value">The <see cref="Color" /> value to write.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        // Color.Empty stands for "no color" and is written as null.
        if (value == Color.Empty)
        {
            // Write a null value to the JSON writer.
            writer.WriteNullValue();

            // We're done, return early.
            return;
        }

        // We're done, convert the Color object to a string representation of the RGB color value.
        writer.WriteStringValue($"rgb({value.R}, {value.G}, {value.B})");
    }
}
