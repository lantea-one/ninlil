// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This type reads a domain name embedded inside a record's own RDATA that may use a DNS
///     message compression pointer (RFC 1035 §4.1.4) -- something <see cref="UncompressedDnsNameCodec" />
///     alone cannot resolve, since a pointer's target can live anywhere earlier in the message,
///     outside the isolated RDATA slice a codec is normally handed. <see cref="DnsMessageReader" />
///     constructs one of these, giving it the full message buffer a record came from and that
///     record's own RDATA starting offset within it, before invoking a codec's
///     decompression-aware <see cref="IDnsRecordDataCodec.Read(System.ReadOnlySpan{byte},DecompressingNameReader)" />
///     overload.
/// </summary>
public readonly ref struct DecompressingNameReader
{
    private readonly ReadOnlySpan<byte> _messageBuffer;
    private readonly int _rdataStartOffset;

    /// <summary>
    ///     This constructor pairs a full message buffer with the RDATA offset within it a
    ///     codec's own, RDATA-relative name offsets should be translated against.
    /// </summary>
    /// <param name="messageBuffer">The complete message the record being read came from.</param>
    /// <param name="rdataStartOffset">The offset, within <paramref name="messageBuffer" />, that record's own RDATA starts at.</param>
    internal DecompressingNameReader(ReadOnlySpan<byte> messageBuffer, int rdataStartOffset)
    {
        _messageBuffer = messageBuffer;
        _rdataStartOffset = rdataStartOffset;
    }

    /// <summary>
    ///     This method reads a single name, following a compression pointer against the full
    ///     message buffer this reader was constructed with if the name uses one.
    /// </summary>
    /// <param name="offset">
    ///     On entry, the position, relative to the record's own RDATA, the name starts at. On
    ///     return, the position immediately after it.
    /// </param>
    /// <returns>The decoded name.</returns>
    public DnsName Read(ref int offset)
    {
        // A codec only ever knows offsets relative to its own RDATA slice, but resolving a
        // compression pointer requires an offset into the full message -- translate in, let
        // the shared reader do the actual (possibly pointer-following) work, then translate
        // the resulting position back to RDATA-relative before returning it to the codec.
        int absoluteOffset = _rdataStartOffset + offset;
        DnsName name = DnsMessageReader.ReadName(_messageBuffer, ref absoluteOffset);
        offset = absoluteOffset - _rdataStartOffset;
        return name;
    }
}
