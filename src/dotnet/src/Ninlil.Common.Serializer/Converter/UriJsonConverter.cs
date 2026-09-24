using System.Text.Json;
using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This class provides a JSON converter for an <see cref="Uri" />.
/// </summary>
public class UriJsonConverter : JsonConverter<Uri>
{
    /// <summary>
    ///     This method reads and converts the JSON to an <see cref="Uri" />.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">The <see cref="Type" /> of object to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    /// <returns>>The converted <see cref="Uri" />.</returns>
    public override Uri Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) =>
        Uri.TryCreate(reader.GetString(), UriKind.RelativeOrAbsolute, out var address)
            ? address
            : null;

    /// <summary>
    ///     This method writes a <see cref="Uri" /> as JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter" /> to write to.</param>
    /// <param name="value">The <see cref="Uri" /> value to write.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options)
    {
        // Check for a null value.
        if (value is null)
        {
            // Write a null value to the JSON writer.
            writer.WriteNullValue();

            // We're done, return early.
            return;
        }

        // Write the URI's string representation to the JSON writer.
        writer.WriteStringValue(value.ToString());
    }
}
