using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class converts between stored <see cref="RecordEntity" /> rows (record data as
///     JSON) and wire records (RDATA). The resolver, zone transfer, signing and the transfer
///     pull all use it.
/// </summary>
public static class RecordEntityCodec
{
    /// <summary>
    ///     This method converts stored records that share one owner name into wire records.
    /// </summary>
    /// <param name="records">The stored records to convert.</param>
    /// <param name="ownerName">The owner name to give every converted record.</param>
    /// <returns>The converted records. A record whose type has no codec is skipped.</returns>
    public static List<DnsResourceRecord> ToWireRecords(
        IReadOnlyList<RecordEntity> records,
        DnsName ownerName
    ) =>
        [
            .. records
                .Select(record => ToWireRecord(record, ownerName))
                .Where(record => record is not null),
        ];

    /// <summary>
    ///     This method converts one stored record to wire form: its JSON data is read into the
    ///     typed model for its type and written as RDATA. A type with no registered codec returns
    ///     <see langword="null" /> rather than a malformed record or an exception.
    /// </summary>
    /// <param name="record">The stored record to convert.</param>
    /// <param name="ownerName">
    ///     The owner name to give the converted record. It differs from the stored name for a
    ///     wildcard record, which is stored as <c>*.example.com</c> but served under the queried
    ///     name.
    /// </param>
    /// <returns>
    ///     The converted record, or <see langword="null" /> if no codec is registered for
    ///     <paramref name="record" />'s type.
    /// </returns>
    public static DnsResourceRecord ToWireRecord(RecordEntity record, DnsName ownerName)
    {
        if (!DnsRecordDataCodecRegistry.TryGetCodec(record.Type, out IDnsRecordDataCodec codec))
            return null;

        return new DnsResourceRecord(
            ownerName,
            record.Type,
            record.Class,
            record.TimeToLiveSeconds,
            codec.Write(DecodeTypedData(record))
        );
    }

    /// <summary>
    ///     This method converts a wire record (from a zone transfer, for example) into a
    ///     <see cref="RecordEntity" /> to store, the reverse of <see cref="ToWireRecord" />: the
    ///     RDATA is read into its typed model and written as JSON.
    /// </summary>
    /// <param name="record">The wire-form record to convert.</param>
    /// <param name="zoneId">The identifier of the zone this record belongs to.</param>
    /// <returns>
    ///     The new, unsaved entity (with a new <see cref="EntityBase.Id" />), or
    ///     <see langword="null" /> if no codec is registered for
    ///     <paramref name="record" />'s type.
    /// </returns>
    public static RecordEntity FromWireRecord(DnsResourceRecord record, Guid zoneId)
    {
        if (!DnsRecordDataCodecRegistry.TryGetCodec(record.Type, out IDnsRecordDataCodec codec))
            return null;

        return new RecordEntity
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = record.Name.ToString().TrimEnd('.'),
            Type = record.Type,
            Class = record.Class,
            TimeToLiveSeconds = record.TimeToLive,
            // Pass the runtime type explicitly. codec.Read returns the empty marker interface
            // IDnsRecordData, so the one-argument overload would bind to the generic
            // Serialize<IDnsRecordData> and write "{}", losing the data.
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                codec.DataType,
                codec.Read(record.Data)
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    ///     This method reads a stored record's JSON data into its typed model, for code that needs
    ///     a value from it (an NS name, a CNAME target, the SOA minimum).
    /// </summary>
    /// <param name="record">The stored record to decode.</param>
    /// <returns>
    ///     The decoded, strongly-typed record data, or <see langword="null" /> if no codec is
    ///     registered for <paramref name="record" />'s type.
    /// </returns>
    public static IDnsRecordData DecodeTypedData(RecordEntity record) =>
        DnsRecordDataCodecRegistry.TryGetCodec(record.Type, out IDnsRecordDataCodec codec)
            ? (IDnsRecordData)
                Ninlil.Common.Serializer.JsonSerializer.Deserialize(codec.DataType, record.DataJson)
            : null;
}
