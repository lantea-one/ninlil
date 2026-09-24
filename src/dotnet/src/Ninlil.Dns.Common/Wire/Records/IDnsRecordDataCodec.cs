using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This interface converts a single record type's RDATA between the raw bytes RFC 1035 and
///     its type's defining RFC describe and a strongly-typed <see cref="IDnsRecordData" />
///     model. <see cref="DnsRecordDataCodecRegistry" /> holds one implementation of this per
///     supported <see cref="DnsResourceRecordTypeEnum" />.
///     <para>
///         A record type whose RDATA embeds a domain name (an NS record's target, for example) is
///         written by Ninlil's own <see cref="Wire.DnsMessageWriter" /> uncompressed, always
///         -- RFC 1035-era types technically permit compressing a name inside RDATA, but nothing
///         Ninlil serves ever needs the space saving. A message arriving from elsewhere (a
///         real-world root, TLD, or authoritative server this project's own recursive resolver
///         queries, most commonly) may still compress one, though, so the decompression-aware
///         <see cref="Read(System.ReadOnlySpan{byte},DecompressingNameReader)" /> overload exists
///         for <see cref="Wire.DnsMessageReader" /> to call instead, while it still has the full
///         message buffer a compression pointer might point into.
///     </para>
/// </summary>
public interface IDnsRecordDataCodec
{
    /// <summary>
    ///     This property contains the record type this codec reads and writes.
    /// </summary>
    DnsResourceRecordTypeEnum RecordType { get; }

    /// <summary>
    ///     This property contains the concrete <see cref="IDnsRecordData" /> implementation this
    ///     codec reads and writes -- for example, <see cref="ARecordDataCodec" /> reports
    ///     <c>typeof(ARecordData)</c>. This is what lets a caller that only has a record's data
    ///     stored as JSON, alongside its <see cref="DnsResourceRecordTypeEnum" />, know which
    ///     concrete type to deserialize that JSON into before this codec ever sees it.
    /// </summary>
    Type DataType { get; }

    /// <summary>
    ///     This property reports whether this record type's RDATA can ever embed a domain name
    ///     that might itself use a compression pointer (RFC 1035 §4.1.4) -- <see
    ///     cref="Wire.DnsMessageReader" /> only pays for the decompression-aware
    ///     <see cref="Read(System.ReadOnlySpan{byte},DecompressingNameReader)" /> round-trip for a
    ///     codec that reports <see langword="true" /> here; every other codec's RDATA is kept
    ///     exactly as sliced off the wire, unmodified. A codec overriding
    ///     <see cref="Read(System.ReadOnlySpan{byte},DecompressingNameReader)" /> must also
    ///     override this to <see langword="true" />, or the override is never actually called.
    ///     Defaults to <see langword="false" />, correct as-is for a record type (TXT, A, AAAA,
    ///     CAA, and most others) whose RDATA embeds no domain name at all.
    /// </summary>
    bool CanEmbedCompressedName => false;

    /// <summary>
    ///     This method decodes a record's RDATA into its strongly-typed model.
    /// </summary>
    /// <param name="rdata">The record's RDATA, exactly as it appears on the wire.</param>
    /// <returns>The decoded, strongly-typed record data.</returns>
    IDnsRecordData Read(ReadOnlySpan<byte> rdata);

    /// <summary>
    ///     This method decodes a record's RDATA into its strongly-typed model, resolving a
    ///     compression pointer against the full message the RDATA came from should any name
    ///     embedded in it use one. Only ever called by <see cref="Wire.DnsMessageReader" /> when
    ///     <see cref="CanEmbedCompressedName" /> is <see langword="true" />; the default
    ///     implementation simply ignores <paramref name="decompressor" /> and calls
    ///     <see cref="Read(System.ReadOnlySpan{byte})" />.
    /// </summary>
    /// <param name="rdata">The record's RDATA, exactly as it appears on the wire.</param>
    /// <param name="decompressor">A reader that can resolve a compression pointer inside <paramref name="rdata" /> against the full message it came from.</param>
    /// <returns>The decoded, strongly-typed record data.</returns>
    IDnsRecordData Read(ReadOnlySpan<byte> rdata, DecompressingNameReader decompressor) =>
        Read(rdata);

    /// <summary>
    ///     This method encodes a strongly-typed record data model into RDATA bytes.
    /// </summary>
    /// <param name="data">The strongly-typed record data to encode.</param>
    /// <returns>The encoded RDATA, ready to embed in a <see cref="Wire.DnsResourceRecord" />.</returns>
    byte[] Write(IDnsRecordData data);
}
