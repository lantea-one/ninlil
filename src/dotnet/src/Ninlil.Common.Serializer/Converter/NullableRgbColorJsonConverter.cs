using System.Drawing;
using System.Text.Json;
using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This System.Text.Json converter writes a nullable <see cref="Color" /> as a CSS
///     <c>rgb(r, g, b)</c> string and reads it back through <see cref="RgbColorParsingHelper" />.
///     Null, blank or unparseable text reads as <see langword="null" />; alpha is dropped.
/// </summary>
public class NullableRgbColorJsonConverter : JsonConverter<Color?>
{
    /// <summary>
    ///     This method reads and converts the JSON to an <see cref="Nullable{Color}" />.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">The <see cref="Type" /> of object to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    /// <returns>The converted <see cref="Nullable{Color}" />.</returns>
    public override Color? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Localize the color string from the JSON reader.
        var rgbString = reader.GetString();

        // If the RGB string is null or empty, return null. Otherwise, parse the rgb(r, g, b)
        // string into a Color, falling back to null if it doesn't match the expected format
        // (rather than throwing).
        return rgbString?.Trim() is null or { Length: 0 } ? null
            : RgbColorParsingHelper.TryParse(rgbString, out Color color) ? color
            : null;
    }

    /// <summary>
    ///     This method writes a <see cref="Nullable{Color}" /> as JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter" /> to write to.</param>
    /// <param name="value">The <see cref="Nullable{Color}" /> value to write.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    public override void Write(Utf8JsonWriter writer, Color? value, JsonSerializerOptions options)
    {
        // Check for a null value.
        if (value is null)
        {
            // Write a null value to the JSON writer.
            writer.WriteNullValue();

            // We're done, return early.
            return;
        }

        // We're done, convert the Color object to a string representation of the RGB color value.
        writer.WriteStringValue($"rgb({value.Value.R}, {value.Value.G}, {value.Value.B})");
    }
}
