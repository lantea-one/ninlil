using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This interface converts one record type's RDATA between its wire bytes (as RFC 1035 and the
///     type's RFC define them) and a typed <see cref="IDnsRecordData" /> model.
///     <see cref="DnsRecordDataCodecRegistry" /> holds one implementation per supported
///     <see cref="DnsResourceRecordTypeEnum" />.
///     <para>
///         Ninlil always writes a domain name inside RDATA (an NS record's target, for example)
///         uncompressed. RFC 1035 types allow compression there, but Ninlil does not need the
///         space. Messages from other servers, most often the root, TLD and authoritative servers
///         the recursive resolver queries, may still compress such names. So
///         <see cref="Wire.DnsMessageReader" /> calls the
///         <see cref="Read(System.ReadOnlySpan{byte},DecompressingNameReader)" /> overload while it
///         still has the full message a pointer could point into.
///     </para>
/// </summary>
public interface IDnsRecordDataCodec
{
    /// <summary>
    ///     This property contains the record type this codec reads and writes.
    /// </summary>
    DnsResourceRecordTypeEnum RecordType { get; }

    /// <summary>
    ///     This property contains the <see cref="IDnsRecordData" /> type this codec reads and writes;
    ///     for example, <see cref="ARecordDataCodec" /> reports <c>typeof(ARecordData)</c>. A caller
    ///     holding a record's data as JSON plus its <see cref="DnsResourceRecordTypeEnum" /> uses it
    ///     to know which type to deserialize the JSON into.
    /// </summary>
    Type DataType { get; }

    /// <summary>
    ///     This property reports whether this type's RDATA can contain a domain name that uses a
    ///     compression pointer (RFC 1035 §4.1.4). <see cref="Wire.DnsMessageReader" /> only takes the
    ///     decompression round trip through
    ///     <see cref="Read(System.ReadOnlySpan{byte},DecompressingNameReader)" /> for codecs that
    ///     return <see langword="true" />; other RDATA is kept as sliced from the wire. A codec that
    ///     overrides that overload must also return <see langword="true" /> here, or the override is
    ///     never called. The default, <see langword="false" />, suits types with no embedded name
    ///     (TXT, A, AAAA, CAA and most others).
    /// </summary>
    bool CanEmbedCompressedName => false;

    /// <summary>
    ///     This method decodes a record's RDATA into its typed model.
    /// </summary>
    /// <param name="rdata">The record's RDATA, as it appears on the wire.</param>
    /// <returns>The decoded, strongly-typed record data.</returns>
    IDnsRecordData Read(ReadOnlySpan<byte> rdata);

    /// <summary>
    ///     This method decodes a record's RDATA into its typed model, resolving any compression
    ///     pointer in an embedded name against the full message. <see cref="Wire.DnsMessageReader" />
    ///     calls it only when <see cref="CanEmbedCompressedName" /> is <see langword="true" />. The
    ///     default implementation ignores <paramref name="decompressor" /> and calls
    ///     <see cref="Read(System.ReadOnlySpan{byte})" />.
    /// </summary>
    /// <param name="rdata">The record's RDATA, as it appears on the wire.</param>
    /// <param name="decompressor">A reader that can resolve a compression pointer inside <paramref name="rdata" /> against the full message it came from.</param>
    /// <returns>The decoded, strongly-typed record data.</returns>
    IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor) =>
        Read(rdata);

    /// <summary>
    ///     This method encodes a typed record data model into RDATA bytes.
    /// </summary>
    /// <param name="data">The typed record data to encode.</param>
    /// <returns>The encoded RDATA, ready to embed in a <see cref="Wire.DnsResourceRecord" />.</returns>
    byte[] Write(IDnsRecordData data);
}
