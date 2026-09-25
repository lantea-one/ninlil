using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Common.Serializer.Model;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This System.Text.Json converter writes a structured string model (see
///     <see cref="IStructuredStringModel" />) as its string form and parses it back with
///     <see cref="StructuredStringModel.TryParse{TModel}(string, out TModel)" />. Blank, unparseable or
///     null-valued input reads as <see langword="null" />, and an empty model is written as JSON null.
/// </summary>
/// <typeparam name="TObject">The type of object to convert.</typeparam>
public class StructuredStringJsonConverter<TObject> : JsonConverter<TObject>
    where TObject : class, IStructuredStringModel, new()
{
    /// <summary>
    ///     This method reads the model from a JSON string.
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
    ///     This method writes the model as a JSON string, or null when its string form is blank.
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
