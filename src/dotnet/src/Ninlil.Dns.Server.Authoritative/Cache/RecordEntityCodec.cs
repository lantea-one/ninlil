using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Cache;

/// <summary>
///     This class converts a stored <see cref="RecordEntity" /> into its wire form, shared by
///     everything in this project that needs to serve stored records back out over the wire --
///     <see cref="Resolution.AuthoritativeQueryResolver" /> for ordinary query answers, and a
///     zone transfer for a complete zone dump.
/// </summary>
public static class RecordEntityCodec
{
    /// <summary>
    ///     This method decodes each of a set of stored records into its wire form, all sharing
    ///     one owner name.
    /// </summary>
    /// <param name="records">The stored records to convert.</param>
    /// <param name="ownerName">The owner name every one of <paramref name="records" /> shares.</param>
    /// <returns>The converted records. A record with no registered codec is silently skipped.</returns>
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
    ///     This method converts a single stored record into its wire form, decoding its stored
    ///     JSON data into the strongly-typed model its record type's codec expects and
    ///     re-encoding that model as RDATA. A record whose type has no registered codec (a
    ///     DNSSEC-signing-related type, for example, not currently implemented) is
    ///     skipped rather than served malformed or left to throw partway through building a
    ///     response.
    /// </summary>
    /// <param name="record">The stored record to convert.</param>
    /// <param name="ownerName">
    ///     The owner name to give the converted record. Not always simply
    ///     <c><see cref="DnsName" />.Parse(<paramref name="record" />.Name)</c> -- a wildcard
    ///     record's own stored name is literally <c>*.example.com</c>, but the record it answers
    ///     for must be served back under whichever concrete name was actually queried.
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
    ///     This method converts a record exactly as it arrived on the wire -- from a pulled zone
    ///     transfer, for example -- into a <see cref="RecordEntity" /> ready to be stored, doing
    ///     the reverse of <see cref="ToWireRecord" />: decoding the RDATA into its strongly-typed
    ///     model and re-encoding that model as JSON.
    /// </summary>
    /// <param name="record">The wire-form record to convert.</param>
    /// <param name="zoneId">The identifier of the zone this record belongs to.</param>
    /// <returns>
    ///     The converted, not-yet-persisted entity (a fresh <see cref="EntityBase.Id" /> is
    ///     assigned), or <see langword="null" /> if no codec is registered for
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
            // Serialize(Type, object) is called explicitly here, rather than the single-argument
            // Serialize(object) overload, because codec.Read's static return type is the empty
            // marker interface IDnsRecordData: passing it to the single-argument overload would
            // resolve to the generic Serialize<TSource>(TSource) instead (an exact-type-match
            // generic instantiation beats a same-arity overload that only fits via an implicit
            // reference conversion), serializing based on IDnsRecordData's own (nonexistent)
            // properties rather than the value's real runtime type -- silently producing "{}"
            // and losing the record's actual data entirely.
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                codec.DataType,
                codec.Read(record.Data)
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    ///     This method decodes a stored record's own JSON data into its strongly-typed model,
    ///     used where code needs to inspect a record's *meaning* -- an NS record's nameserver, a
    ///     CNAME's target, a SOA's negative-caching minimum -- rather than just serve it as-is.
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
