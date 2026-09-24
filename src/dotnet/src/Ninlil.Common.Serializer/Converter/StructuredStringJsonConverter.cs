using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This class maintains our custom JSON converter for string-like values.
/// </summary>
/// <typeparam name="TObject">The type of object to convert.</typeparam>
public class StructuredStringJsonConverter<TObject> : JsonConverter<TObject>
    where TObject : class, IStructuredStringModel, new()
{
    /// <summary>
    ///     This method reads the string-like value from JSON.
    /// </summary>
    /// <param name="reader">The reference instance of the JSON reader.</param>
    /// <param name="typeToConvert">The type to be converted.</param>
    /// <param name="options">The JSON serializer options.</param>
    /// <returns>The deserialized typed value.</returns>
    public override TObject Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        // Read the string from the serializer.
        string value = reader.GetString()?.Trim();

        // Check for a null or empty string.
        if (value is null or { Length: 0 })
            return null;

        // Try to parse the string into our structured string model.
        // If parsing fails or the result is null, return null.
        return
            StructuredStringModel.TryParse(value, out TObject result) && result?.IsNull() is false
            ? result
            : null;
    }

    /// <summary>
    ///     This method writes a string-like value to a JSON string.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="value">The typed value to serialize.</param>
    /// <param name="options">The JSON serializer options.</param>
    public override void Write(Utf8JsonWriter writer, TObject value, JsonSerializerOptions options)
    {
        // Localize the string value to write.
        string valueToWrite = value?.ToString()?.Trim();

        // Check for a null or empty string.
        if (valueToWrite is null or { Length: 0 })
        {
            // Write a null value to the JSON output.
            writer.WriteNullValue();

            // We're done, so return from the method.
            return;
        }

        // Write the string value to the JSON output.
        writer.WriteStringValue(valueToWrite);
    }
}
