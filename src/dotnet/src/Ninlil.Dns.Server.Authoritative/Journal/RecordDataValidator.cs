using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class validates record data supplied as JSON through the REST API against its type's
///     <see cref="IDnsRecordDataCodec.DataType" />, and returns it in the same canonical JSON form
///     <see cref="Cache.RecordEntityCodec" /> stores. It works on JSON only and never touches the
///     RDATA wire codecs. Only REST write paths (<c>RecordsController</c> and similar) call it, so
///     it throws <see cref="ValidationException" /> for bad input.
/// </summary>
public static class RecordDataValidator
{
    /// <summary>
    ///     This method validates and canonicalizes record data given as a
    ///     <see cref="JsonElement" />, as a REST request body's <c>data</c> field arrives.
    /// </summary>
    /// <param name="type">The record type <paramref name="data" /> is declared to hold data for.</param>
    /// <param name="data">The record's data as supplied.</param>
    /// <returns>
    ///     The canonical (PascalCase) JSON form of <paramref name="data" />, as stored.
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
    ///     This method is <see cref="EncodeDataOrThrow" /> for raw JSON text, for re-validating a
    ///     stored record's <c>DataJson</c> against a new type (a <c>PATCH</c> that changes
    ///     <c>type</c> but not <c>data</c>).
    /// </summary>
    /// <param name="type">The record type <paramref name="json" /> is declared to hold data for.</param>
    /// <param name="json">The record's data, as raw JSON text.</param>
    /// <returns>
    ///     The canonical (PascalCase) JSON form of <paramref name="json" />, as stored.
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
