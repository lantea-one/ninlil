using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class parses a HAProxy-style PROXY protocol header (both the v1 text form and the v2
///     binary form) directly off a <see cref="Stream" />, per the public specification at
///     <c>https://www.haproxy.org/download/2.8/doc/proxy-protocol.txt</c> -- reading and
///     implementing this freely-published wire format is the same "RFC/public-spec, not GPL
///     source" precedent every other protocol in this project already follows, never a
///     clean-room concern.
///     <para>
///         Every read here happens against the raw, still-plaintext connection stream, before any
///         TLS handshake begins -- the PROXY protocol header is always the very first bytes a
///         proxy sends, ahead of whatever it is actually relaying (here, a TLS <c>ClientHello</c>).
///     </para>
/// </summary>
public static class ProxyProtocolReader
{
    /// <summary>
    ///     This constant contains the v2 binary header's own fixed 12-byte signature -- a value no
    ///     legitimate TLS <c>ClientHello</c> or v1 text header could ever begin with, which is
    ///     what lets <see cref="ReadAsync" /> tell the two wire forms apart from their very first
    ///     byte alone.
    /// </summary>
    private static readonly byte[] V2Signature =
    [
        0x0D,
        0x0A,
        0x0D,
        0x0A,
        0x00,
        0x0D,
        0x0A,
        0x51,
        0x55,
        0x49,
        0x54,
        0x0A,
    ];

    /// <summary>
    ///     This constant contains the v2 binary header's own fixed length -- the 12-byte
    ///     signature, one version/command byte, one address-family/transport-protocol byte, and a
    ///     2-byte big-endian length field for whatever variable-length address block (and any
    ///     trailing TLVs) follows.
    /// </summary>
    private const int V2FixedHeaderLength = 16;

    /// <summary>
    ///     This constant bounds how many bytes a v2 header's own variable-length address block may
    ///     claim to carry -- the field itself is a 2-byte unsigned integer, so this is already the
    ///     hard protocol maximum, not an arbitrary further restriction.
    /// </summary>
    private const int MaximumV2AddressBlockLength = ushort.MaxValue;

    /// <summary>
    ///     This constant bounds how many bytes a v1 text header may occupy before its own
    ///     terminating CRLF must have appeared -- the specification's own stated maximum buffer
    ///     requirement (a 104-byte TCP6 line plus its own CRLF, rounded up), used here to decide
    ///     when to give up looking for one and declare the header invalid rather than reading
    ///     forever.
    /// </summary>
    private const int MaximumV1HeaderBytes = 108;

    /// <summary>
    ///     This method reads and parses a single PROXY protocol header from the start of
    ///     <paramref name="stream" />, consuming exactly the bytes the header itself occupies (and
    ///     no more) so a caller can immediately continue reading whatever the header was itself
    ///     prefixed onto (here, a TLS handshake) from the very same stream.
    /// </summary>
    /// <param name="stream">The raw, not-yet-TLS-wrapped connection stream to read from.</param>
    /// <param name="cancellationToken">A token this read honors, including a caller-imposed header-read timeout.</param>
    /// <returns>
    ///     See <see cref="ProxyProtocolParseResult" />'s own doc comment for the three possible
    ///     outcomes.
    /// </returns>
    public static async Task<ProxyProtocolParseResult> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken
    )
    {
        int firstByte = await ReadByteAsync(stream, cancellationToken);

        // The connection closed before a single byte of the expected header ever arrived.
        if (firstByte < 0)
            return ProxyProtocolParseResult.Failed;

        // The v2 binary signature's own first byte (0x0D) can never begin a v1 text header (which
        // must begin with the literal ASCII letter 'P'), so this single byte is enough to commit
        // to one wire form or the other.
        return firstByte == V2Signature[0]
            ? await ReadV2Async(stream, (byte)firstByte, cancellationToken)
            : await ReadV1Async(stream, (byte)firstByte, cancellationToken);
    }

    /// <summary>
    ///     This method reads and parses a v1 text header, accumulating bytes until a CRLF
    ///     terminator is found or <see cref="MaximumV1HeaderBytes" /> is exceeded without one --
    ///     per the specification's own guidance, a line this long without a CRLF is declared
    ///     invalid outright, never read further.
    /// </summary>
    private static async Task<ProxyProtocolParseResult> ReadV1Async(
        Stream stream,
        byte firstByte,
        CancellationToken cancellationToken
    )
    {
        byte[] buffer = new byte[MaximumV1HeaderBytes];
        buffer[0] = firstByte;
        int length = 1;

        while (length < buffer.Length)
        {
            int next = await ReadByteAsync(stream, cancellationToken);

            // The connection closed partway through what should have been a complete header.
            if (next < 0)
                return ProxyProtocolParseResult.Failed;

            buffer[length++] = (byte)next;

            if (length >= 2 && buffer[length - 2] == (byte)'\r' && buffer[length - 1] == (byte)'\n')
                return ParseV1Line(Encoding.ASCII.GetString(buffer, 0, length - 2));
        }

        // No CRLF appeared within the defensive maximum -- declare the header invalid, per the
        // specification's own explicit "if the CRLF sequence is not found ... the receiver should
        // declare the line invalid" guidance.
        return ProxyProtocolParseResult.Failed;
    }

    /// <summary>
    ///     This method parses a v1 header's own already-extracted line (with its terminating CRLF
    ///     already stripped) into the address it claims, if any.
    /// </summary>
    private static ProxyProtocolParseResult ParseV1Line(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0 || tokens[0] != "PROXY")
            return ProxyProtocolParseResult.Failed;

        // "PROXY UNKNOWN" (with or without any further tokens after it, per real-world proxy
        // implementations that still append placeholder addresses/ports even for this case) means
        // the proxy deliberately declines to disclose an address -- fall back to the real
        // connection endpoint without treating this as an error.
        if (tokens.Length >= 2 && tokens[1] == "UNKNOWN")
            return ProxyProtocolParseResult.WithoutClaimedAddress;

        if (tokens.Length != 6)
            return ProxyProtocolParseResult.Failed;

        string family = tokens[1];

        if (family != "TCP4" && family != "TCP6")
            return ProxyProtocolParseResult.Failed;

        if (
            !IPAddress.TryParse(tokens[2], out IPAddress sourceAddress)
            || !IPAddress.TryParse(tokens[3], out _)
        )
            return ProxyProtocolParseResult.Failed;

        bool addressFamilyMatchesToken =
            (family == "TCP4" && sourceAddress.AddressFamily == AddressFamily.InterNetwork)
            || (family == "TCP6" && sourceAddress.AddressFamily == AddressFamily.InterNetworkV6);

        if (!addressFamilyMatchesToken)
            return ProxyProtocolParseResult.Failed;

        if (!ushort.TryParse(tokens[4], out _) || !ushort.TryParse(tokens[5], out _))
            return ProxyProtocolParseResult.Failed;

        return ProxyProtocolParseResult.WithClaimedAddress(sourceAddress);
    }

    /// <summary>
    ///     This method reads and parses a v2 binary header: the remainder of the 16-byte fixed
    ///     header (the first signature byte was already consumed by <see cref="ReadAsync" />),
    ///     then exactly as many further bytes as the header's own length field claims, regardless
    ///     of whether this method actually needs every one of them -- a v2 header may carry
    ///     trailing TLVs this project has no use for, and the stream must still be advanced past
    ///     them so whatever follows (the TLS handshake) starts at the right offset.
    /// </summary>
    private static async Task<ProxyProtocolParseResult> ReadV2Async(
        Stream stream,
        byte firstSignatureByte,
        CancellationToken cancellationToken
    )
    {
        byte[] header = new byte[V2FixedHeaderLength];
        header[0] = firstSignatureByte;

        if (!await ReadExactAsync(stream, header, 1, cancellationToken))
            return ProxyProtocolParseResult.Failed;

        for (int index = 0; index < V2Signature.Length; index++)
        {
            if (header[index] != V2Signature[index])
                return ProxyProtocolParseResult.Failed;
        }

        byte versionAndCommand = header[12];
        int version = versionAndCommand >> 4;
        int command = versionAndCommand & 0x0F;

        // Only protocol version 2 is understood -- a future version this project has never seen
        // is treated the same as any other malformed header, per the specification's own "any
        // sequence which does not exactly match the protocol must ... abort the connection"
        // guidance, rather than guessed at.
        if (version != 0x2)
            return ProxyProtocolParseResult.Failed;

        byte addressFamilyAndTransportProtocol = header[13];
        ushort addressBlockLength = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(14, 2));

        if (addressBlockLength > MaximumV2AddressBlockLength)
            return ProxyProtocolParseResult.Failed;

        byte[] addressBlock = addressBlockLength == 0 ? [] : new byte[addressBlockLength];

        if (
            addressBlockLength > 0
            && !await ReadExactAsync(stream, addressBlock, 0, cancellationToken)
        )
            return ProxyProtocolParseResult.Failed;

        // A LOCAL connection -- the proxy's own health check, or an explicit "no address to
        // disclose" signal -- carries no usable client address, regardless of whatever the
        // address block itself happens to contain; its contents must be entirely ignored.
        if (command == 0x0)
            return ProxyProtocolParseResult.WithoutClaimedAddress;

        if (command != 0x1)
            return ProxyProtocolParseResult.Failed;

        return addressFamilyAndTransportProtocol switch
        {
            // TCP over IPv4.
            0x11 => ParseV2Ipv4AddressBlock(addressBlock),
            // TCP over IPv6.
            0x21 => ParseV2Ipv6AddressBlock(addressBlock),
            // Every other family/transport combination (UDP, Unix sockets, AF_UNSPEC) carries no
            // address this TCP-only listener can use -- fall back to the real connection endpoint
            // rather than treating a structurally valid header this project simply has no use for
            // as an error.
            _ => ProxyProtocolParseResult.WithoutClaimedAddress,
        };
    }

    /// <summary>
    ///     This method reads the 12-byte TCP-over-IPv4 address block (source address, destination
    ///     address, source port, destination port, all network byte order) and returns the claimed
    ///     source address.
    /// </summary>
    private static ProxyProtocolParseResult ParseV2Ipv4AddressBlock(byte[] addressBlock)
    {
        if (addressBlock.Length < 12)
            return ProxyProtocolParseResult.Failed;

        return ProxyProtocolParseResult.WithClaimedAddress(
            new IPAddress(addressBlock.AsSpan(0, 4).ToArray())
        );
    }

    /// <summary>
    ///     This method reads the 36-byte TCP-over-IPv6 address block (source address, destination
    ///     address, source port, destination port, all network byte order) and returns the claimed
    ///     source address.
    /// </summary>
    private static ProxyProtocolParseResult ParseV2Ipv6AddressBlock(byte[] addressBlock)
    {
        if (addressBlock.Length < 36)
            return ProxyProtocolParseResult.Failed;

        return ProxyProtocolParseResult.WithClaimedAddress(
            new IPAddress(addressBlock.AsSpan(0, 16).ToArray())
        );
    }

    /// <summary>
    ///     This method reads a single byte from a stream, reporting a clean disconnect as
    ///     <c>-1</c> rather than throwing.
    /// </summary>
    private static async Task<int> ReadByteAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] single = new byte[1];
        int read = await stream.ReadAsync(single.AsMemory(0, 1), cancellationToken);
        return read == 0 ? -1 : single[0];
    }

    /// <summary>
    ///     This method fills <paramref name="buffer" /> completely, starting at
    ///     <paramref name="offset" />, reporting a premature disconnect as <see langword="false" />
    ///     rather than throwing.
    /// </summary>
    private static async Task<bool> ReadExactAsync(
        Stream stream,
        byte[] buffer,
        int offset,
        CancellationToken cancellationToken
    )
    {
        int totalRead = offset;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken);

            if (read == 0)
                return false;

            totalRead += read;
        }

        return true;
    }
}
