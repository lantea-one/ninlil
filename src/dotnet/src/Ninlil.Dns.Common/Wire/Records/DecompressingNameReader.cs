// Define our namespace.
namespace Ninlil.Dns.Common.Wire.Records;

/// <summary>
///     This type reads a domain name inside a record's RDATA that may use a message compression
///     pointer (RFC 1035 §4.1.4). <see cref="UncompressedDnsNameCodec" /> cannot follow such a
///     pointer, because its target can be anywhere earlier in the message, outside the RDATA slice
///     a codec is given. <see cref="DnsMessageReader" /> creates one with the full message buffer
///     and the RDATA start offset, then calls the codec's
///     <see cref="IDnsRecordDataCodec.Read(System.ReadOnlySpan{byte},DecompressingNameReader)" />
///     overload.
/// </summary>
public readonly ref struct DecompressingNameReader
{
    private readonly ReadOnlySpan<byte> _messageBuffer;
    private readonly int _rdataStartOffset;

    /// <summary>
    ///     This constructor pairs a full message buffer with the RDATA offset that a codec's
    ///     RDATA-relative name offsets are translated against.
    /// </summary>
    /// <param name="messageBuffer">The complete message the record being read came from.</param>
    /// <param name="rdataStartOffset">The offset in <paramref name="messageBuffer" /> where the record's RDATA starts.</param>
    internal DecompressingNameReader(ReadOnlySpan<byte> messageBuffer, int rdataStartOffset)
    {
        _messageBuffer = messageBuffer;
        _rdataStartOffset = rdataStartOffset;
    }

    /// <summary>
    ///     This method reads one name, following a compression pointer into the full message buffer
    ///     if the name uses one.
    /// </summary>
    /// <param name="offset">
    ///     On entry, the position of the name relative to the RDATA. On return, the position just
    ///     after it.
    /// </param>
    /// <returns>The decoded name.</returns>
    public DnsName Read(ref int offset)
    {
        // A codec knows only RDATA-relative offsets, but a compression pointer needs an offset
        // into the full message. Translate in, let the shared reader follow any pointer, then
        // translate the position back to RDATA-relative for the codec.
        int absoluteOffset = _rdataStartOffset + offset;
        DnsName name = DnsMessageReader.ReadName(_messageBuffer, ref absoluteOffset);
        offset = absoluteOffset - _rdataStartOffset;
        return name;
    }
}
