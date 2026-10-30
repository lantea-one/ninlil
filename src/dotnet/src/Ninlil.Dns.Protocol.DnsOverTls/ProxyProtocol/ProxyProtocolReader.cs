using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

/// <summary>
///     This class parses a HAProxy-style PROXY protocol header (both the v1 text form and the v2
///     binary form) off a <see cref="Stream" />, following the specification "The PROXY protocol,
///     Versions 1 &amp; 2" (Willy Tarreau, HAProxy Technologies,
///     <c>https://www.haproxy.org/download/2.8/doc/proxy-protocol.txt</c>): section 2.1 for the v1
///     text form and section 2.2 for the v2 binary form. It is a published specification rather
///     than source code, so implementing it raises no clean-room concern. <see cref="DnsOverTlsListener" /> calls it.
///     <para>
///         Every read is on the plaintext connection stream, before the TLS handshake: a proxy
///         sends the header first, ahead of what it relays (here, a TLS <c>ClientHello</c>).
///     </para>
/// </summary>
public static class ProxyProtocolReader
{
    /// <summary>
    ///     This field contains the fixed 12-byte signature of a v2 binary header. Its first byte
    ///     cannot begin a v1 text header, which lets <see cref="ReadAsync" /> tell the two forms
    ///     apart from the first byte.
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
    ///     This constant contains the fixed length of a v2 header: the 12-byte signature, a
    ///     version/command byte, an address-family/transport byte, and a 2-byte big-endian length
    ///     of the address block (and any TLVs) that follows.
    /// </summary>
    private const int V2FixedHeaderLength = 16;

    /// <summary>
    ///     This constant contains the largest address block length a v2 header may claim. The
    ///     field is a 2-byte unsigned integer, so this is the protocol maximum and the check
    ///     against it never fails.
    /// </summary>
    private const int MaximumV2AddressBlockLength = ushort.MaxValue;

    /// <summary>
    ///     This constant contains how many bytes a v1 text header may take, CRLF included, before
    ///     it is declared invalid. The specification (section 2.1) puts the longest valid line, CRLF
    ///     included, at 107 characters and says a 108-byte buffer holds it with a trailing zero.
    ///     This reader accepts a CRLF anywhere in the 108 bytes.
    /// </summary>
    private const int MaximumV1HeaderBytes = 108;

    /// <summary>
    ///     This method reads and parses one PROXY protocol header from the start of
    ///     <paramref name="stream" />. It consumes only the header's bytes, so the caller can go on
    ///     to read what follows (here, a TLS handshake) from the same stream.
    /// </summary>
    /// <param name="stream">The raw, not-yet-TLS-wrapped connection stream to read from.</param>
    /// <param name="cancellationToken">The token that cancels the read, including the caller's header timeout.</param>
    /// <returns>
    ///     One of the three outcomes described on <see cref="ProxyProtocolParseResult" />.
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

        // The v2 signature starts with 0x0D and a v1 header with the letter 'P', so the first
        // byte decides which form to parse.
        return firstByte == V2Signature[0]
            ? await ReadV2Async(stream, (byte)firstByte, cancellationToken)
            : await ReadV1Async(stream, (byte)firstByte, cancellationToken);
    }

    /// <summary>
    ///     This method reads and parses a v1 text header, collecting bytes until a CRLF or until
    ///     <see cref="MaximumV1HeaderBytes" /> bytes have arrived without one, in which case the
    ///     line is invalid, as the specification says.
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

        // No CRLF within the maximum: section 2.1 says "If the CRLF sequence is not found in the
        // first 107 characters, the receiver should declare the line invalid".
        return ProxyProtocolParseResult.Failed;
    }

    /// <summary>
    ///     This method parses a v1 header line, without its CRLF, into the address it claims, if
    ///     any. It requires the <c>PROXY</c> keyword, a <c>TCP4</c> or <c>TCP6</c> family matching
    ///     the source address, and valid ports.
    /// </summary>
    private static ProxyProtocolParseResult ParseV1Line(string line)
    {
        string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0 || tokens[0] != "PROXY")
            return ProxyProtocolParseResult.Failed;

        // "PROXY UNKNOWN" (with or without further tokens, since some proxies append placeholder
        // addresses and ports) means the proxy gives no address; use the connection's peer
        // address, without treating it as an error.
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
    ///     This method reads and parses a v2 binary header: the rest of the 16-byte fixed header
    ///     (<see cref="ReadAsync" /> has read the first signature byte), then as many bytes as the
    ///     length field says, whether or not they are all needed. A v2 header may carry TLVs that
    ///     are not used here, and the stream must still move past them so the TLS handshake
    ///     starts at the right place.
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

        // Only version 2 is understood. Section 2.2 says the version "must always be sent as \x2
        // and the receiver must only accept this value", so any other version is malformed.
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

        // A LOCAL command (a proxy health check, or "no address to give") carries no client
        // address; whatever the address block holds is ignored.
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
            // Other combinations (UDP, Unix sockets, AF_UNSPEC) carry no address this TCP
            // listener can use; fall back to the peer address rather than treat a valid header
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
