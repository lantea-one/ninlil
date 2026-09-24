using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

// Define our namespace.
namespace Ninlil.Common.Serializer.Converter;

/// <summary>
///     This class provides a JSON converter for an <see cref="PhysicalAddress" />.
/// </summary>
public class PhysicalAddressJsonConverter : JsonConverter<PhysicalAddress>
{
    /// <summary>
    ///     This method reads and converts the JSON to an <see cref="PhysicalAddress" />.
    /// </summary>
    /// <param name="reader">The <see cref="Utf8JsonReader" /> to read from.</param>
    /// <param name="typeToConvert">The <see cref="Type" /> of object to convert.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    /// <returns>>The converted <see cref="PhysicalAddress" />.</returns>
    public override PhysicalAddress Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => PhysicalAddress.TryParse(reader.GetString(), out var address) ? address : null;

    /// <summary>
    ///     This method writes a <see cref="PhysicalAddress" /> as JSON.
    /// </summary>
    /// <param name="writer">The <see cref="Utf8JsonWriter" /> to write to.</param>
    /// <param name="value">The <see cref="PhysicalAddress" /> value to write.</param>
    /// <param name="options">The <see cref="JsonSerializerOptions" /> to use.</param>
    public override void Write(
        Utf8JsonWriter writer,
        PhysicalAddress value,
        JsonSerializerOptions options
    )
    {
        // Check for a null value.
        if (value is null)
        {
            // Write a null value to the JSON writer.
            writer.WriteNullValue();

            // We're done, return early.
            return;
        }

        // Write the physical address's string representation to the JSON writer.
        writer.WriteStringValue(value.ToString());
    }
}
