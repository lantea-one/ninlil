using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class validates a record's own type-specific data against that type's registered
///     <see cref="IDnsRecordDataCodec" />, and re-serializes it into the same canonical JSON form
///     <see cref="Cache.RecordEntityCodec" /> itself produces for a wire-format record --
///     applied here to REST-supplied JSON instead. This operates entirely on
///     <see cref="Ninlil.Common.Serializer.JsonSerializer" />'s own JSON-to-CLR-object
///     deserialization of a codec's <see cref="IDnsRecordDataCodec.DataType" />, a completely
///     separate code path from <see cref="IDnsRecordDataCodec.Read(System.ReadOnlySpan{byte})" />/
///     <see cref="IDnsRecordDataCodec.Write" />, which decode and encode a record's RDATA to and
///     from its raw wire bytes -- the genuinely hot path every DNS query answer, zone transfer,
///     and dynamic update shares, and which this class never touches. This class's own two
///     methods are called only from a REST-facing write path (<c>RecordsController</c>, most
///     immediately), so a validation failure here is always safe to report as this project's own
///     custom exception hierarchy directly, with no shared DNS-protocol caller whose own error
///     signaling this could disturb.
/// </summary>
public static class RecordDataValidator
{
    /// <summary>
    ///     This method validates and canonicalizes a record's data supplied as a
    ///     <see cref="JsonElement" /> -- the shape a REST request body's own <c>data</c> field
    ///     naturally arrives as once ASP.NET Core's model binder has already parsed the request.
    /// </summary>
    /// <param name="type">The record type <paramref name="data" /> is declared to hold data for.</param>
    /// <param name="data">The record's data, exactly as supplied by the caller.</param>
    /// <returns>
    ///     The canonicalized JSON form of <paramref name="data" />, in the same PascalCase shape
    ///     every other internal consumer of a record's stored data already produces.
    /// </returns>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="type" /> has no registered codec, <paramref name="data" />
    ///     is missing, or <paramref name="data" /> does not match the shape that codec expects.
    /// </exception>
    public static string EncodeDataOrThrow(DnsResourceRecordTypeEnum type, JsonElement data) =>
        EncodeDataFromJsonOrThrow(
            type,
            data.ValueKind == JsonValueKind.Undefined ? null : data.GetRawText()
        );

    /// <summary>
    ///     This method is the raw-JSON-text-accepting core of <see cref="EncodeDataOrThrow" />,
    ///     split out so a caller re-validating an already-stored record's own <c>DataJson</c>
    ///     against a newly-chosen type (a record's own <c>PATCH</c> changing <c>type</c> without
    ///     also supplying a new <c>data</c>, most immediately) can call it directly without
    ///     round-tripping already-serialized JSON text through a <see cref="JsonElement" /> first.
    /// </summary>
    /// <param name="type">The record type <paramref name="json" /> is declared to hold data for.</param>
    /// <param name="json">The record's data, as raw JSON text.</param>
    /// <returns>
    ///     The canonicalized JSON form of <paramref name="json" />, in the same PascalCase shape
    ///     every other internal consumer of a record's stored data already produces.
    /// </returns>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="type" /> has no registered codec, <paramref name="json" />
    ///     is missing, or <paramref name="json" /> does not match the shape that codec expects.
    /// </exception>
    public static string EncodeDataFromJsonOrThrow(DnsResourceRecordTypeEnum type, string json)
    {
        if (!DnsRecordDataCodecRegistry.TryGetCodec(type, out IDnsRecordDataCodec codec))
            throw new ValidationException($"Record type '{type}' is not supported for storage.");

        if (string.IsNullOrWhiteSpace(json))
            throw new ValidationException("'data' is required.");

        object typedData;
        try
        {
            typedData = Ninlil.Common.Serializer.JsonSerializer.Deserialize(codec.DataType, json);
        }
        catch (Exception exception)
        {
            throw new ValidationException(
                $"'data' does not match the shape a '{type}' record expects: {exception.Message}"
            );
        }

        if (typedData is null)
            throw new ValidationException(
                $"'data' does not match the shape a '{type}' record expects."
            );

        return Ninlil.Common.Serializer.JsonSerializer.Serialize(codec.DataType, typedData);
    }
}
