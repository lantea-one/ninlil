using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Serialization;

/// <summary>
///     This class is a JSON converter that reads and writes a <see cref="DnsName" /> in its
///     dot-separated presentation form (<c>www.example.com.</c>) instead of its label bytes. That
///     is the form people use in the admin console and the form the record data models store.
///     <see cref="DnsCommonModuleInitializer" /> registers it. A string that does not parse reads
///     as <see langword="null" />.
/// </summary>
public class DnsNameJsonConverter : JsonConverter<DnsName>
{
    /// <inheritdoc />
    public override DnsName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("A DNS name must be a JSON string.");

        if (!DnsName.TryParse(reader.GetString(), out DnsName name, out string error))
            throw new JsonException(error);

        return name;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DnsName value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value.ToString());
    }
}
