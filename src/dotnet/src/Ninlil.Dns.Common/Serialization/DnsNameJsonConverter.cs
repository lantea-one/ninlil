using System.Text.Json;
using System.Text.Json.Serialization;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Serialization;

/// <summary>
///     This class provides a JSON converter for a <see cref="DnsName" />, reading and writing it
///     as its familiar, dot-separated presentation form (<c>www.example.com.</c>) rather than as
///     its raw label bytes -- the form both a human editing a zone through the admin SPA
///     and this record's own <see cref="Wire.Records.IDnsRecordDataCodec" /> already expect.
/// </summary>
public class DnsNameJsonConverter : JsonConverter<DnsName>
{
    /// <inheritdoc />
    public override DnsName Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    ) => DnsName.TryParse(reader.GetString(), out DnsName name) ? name : null;

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
