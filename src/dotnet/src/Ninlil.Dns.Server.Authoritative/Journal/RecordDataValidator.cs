using System.Reflection;
using System.Text.Json;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Journal;

/// <summary>
///     This class validates record data supplied as JSON through the REST API against its type's
///     <see cref="IDnsRecordDataCodec.DataType" />, and returns it in the same canonical JSON form
///     <see cref="Cache.RecordEntityCodec" /> stores. It reads the JSON into the typed model, refuses a
///     model with an unset field and one its codec cannot write as RDATA, and returns the JSON of
///     the model. For an APL record it also enforces the canonical form RFC 3123 §4 asks
///     senders for (see <see cref="ValidateAplItems" />); the wire codec stays byte-exact, so a
///     transferred, synced or restored record is never judged. Only REST write paths
///     (<c>RecordsController</c> and similar) call it, so it throws
///     <see cref="ValidationException" /> for bad input.
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

        ThrowIfAFieldIsMissing(type, typedData);
        ThrowIfNotWritable(type, codec, typedData);

        if (typedData is AplRecordData apl)
            ValidateAplItems(apl);

        return Ninlil.Common.Serializer.JsonSerializer.Serialize(codec.DataType, typedData);
    }

    /// <summary>
    ///     This method refuses data with a field that is still unset after reading. The JSON reader
    ///     leaves a field null when the property is absent, is spelled differently, or holds a value
    ///     that does not parse (an address that is not an IP address, a name that is not a DNS
    ///     name). A stored record with such a field cannot be written as RDATA, so it would break
    ///     zone signing, transfers and answers. The gateway fields of an IPSECKEY record are the
    ///     only optional reference fields (RFC 4025 §2.3 leaves them out for some gateway types);
    ///     <see cref="ThrowIfNotWritable" /> checks them against the gateway type.
    /// </summary>
    /// <param name="type">The record type, for the message.</param>
    /// <param name="typedData">The data as read.</param>
    /// <exception cref="ValidationException">
    ///     Thrown for the first field that is null; the message names it as it appears in the JSON.
    /// </exception>
    private static void ThrowIfAFieldIsMissing(DnsResourceRecordTypeEnum type, object typedData)
    {
        foreach (
            PropertyInfo property in typedData
                .GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            if (
                (
                    typedData is IpSecKeyRecordData
                    && property.Name
                        is nameof(IpSecKeyRecordData.GatewayAddress)
                            or nameof(IpSecKeyRecordData.GatewayName)
                )
                || property.GetIndexParameters().Length > 0
                || property.PropertyType.IsValueType
                || !property.CanRead
                || property.GetValue(typedData) is not null
            )
                continue;

            string field = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
            throw new ValidationException(
                $"'data' for a '{type}' record needs a valid '{field}' value; it is missing, misspelled or not in the right format."
            );
        }
    }

    /// <summary>
    ///     This method writes the data as RDATA once and refuses it when the codec rejects it (for
    ///     example an IPv6 address in an A record, or an IPSECKEY gateway that does not match its
    ///     gateway type). The stored data is read back through the same codec when the record is
    ///     served, signed or transferred, so data it cannot write must not be stored.
    /// </summary>
    /// <param name="type">The record type, for the message.</param>
    /// <param name="codec">The codec registered for <paramref name="type" />.</param>
    /// <param name="typedData">The data as read.</param>
    /// <exception cref="ValidationException">Thrown when the codec cannot write the data.</exception>
    private static void ThrowIfNotWritable(
        DnsResourceRecordTypeEnum type,
        IDnsRecordDataCodec codec,
        object typedData
    )
    {
        try
        {
            codec.Write((IDnsRecordData)typedData);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new ValidationException(
                $"'data' is not valid for a '{type}' record: {exception.Message}"
            );
        }
    }

    /// <summary>
    ///     This method checks that every IPv4 and IPv6 item of an APL record is in canonical form.
    ///     RFC 3123 §4 says the sender MUST NOT include trailing zero octets in the address part,
    ///     because DNSSEC needs one wire encoding per prefix. It also bounds the prefix length and
    ///     the part by the address family, so a stored item always fits its family. Items of any
    ///     other family pass, since this rule does not know their layout.
    /// </summary>
    /// <param name="apl">The record data to check.</param>
    /// <exception cref="ValidationException">
    ///     Thrown for the first item that is not canonical; the message names the item (1-based).
    /// </exception>
    private static void ValidateAplItems(AplRecordData apl)
    {
        if (apl.Items is null)
            return;

        for (int index = 0; index < apl.Items.Count; index++)
        {
            AplItem item = apl.Items[index];
            string label = $"APL item {index + 1}";

            if (item is null)
                throw new ValidationException($"{label} is missing.");

            // Address family numbers are the IANA ones: 1 is IPv4 (4 octets), 2 is IPv6 (16).
            int addressOctets = item.AddressFamily switch
            {
                1 => 4,
                2 => 16,
                _ => 0,
            };
            if (addressOctets == 0)
                continue;

            byte[] part = item.AddressFamilyDependentPart ?? [];
            if (item.PrefixLength > addressOctets * 8)
                throw new ValidationException(
                    $"{label}: the prefix length {item.PrefixLength} is longer than the {addressOctets * 8} bits of its address family."
                );

            if (part.Length > addressOctets)
                throw new ValidationException(
                    $"{label}: the address part is {part.Length} octets, more than the {addressOctets} of its address family."
                );

            if (part.Length > 0 && part[^1] == 0)
                throw new ValidationException(
                    $"{label}: the address part must not end in a zero octet (RFC 3123 Section 4)."
                );
        }
    }
}
