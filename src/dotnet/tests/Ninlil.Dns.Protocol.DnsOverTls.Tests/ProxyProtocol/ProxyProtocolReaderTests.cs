using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests.ProxyProtocol;

/// <summary>
///     This class tests <see cref="ProxyProtocolReader" /> against hand-built PROXY protocol v1
///     (text) and v2 (binary) byte sequences read from a <see cref="MemoryStream" />; decoding
///     bytes into a <see cref="ProxyProtocolParseResult" /> needs no socket or TLS. The listener
///     side (a TCP connection, trusted and untrusted sources, the TLS handshake that follows) is
///     covered in <c>DnsOverTlsListenerTests</c>.
/// </summary>
public sealed class ProxyProtocolReaderTests
{
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

    private static MemoryStream StreamOf(byte[] bytes) => new(bytes);

    private static byte[] BuildV2Header(
        byte versionAndCommand,
        byte familyAndProtocol,
        byte[] addressBlock
    )
    {
        using MemoryStream buffer = new();
        buffer.Write(V2Signature);
        buffer.WriteByte(versionAndCommand);
        buffer.WriteByte(familyAndProtocol);

        byte[] length = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)addressBlock.Length);
        buffer.Write(length);
        buffer.Write(addressBlock);

        return buffer.ToArray();
    }

    private static byte[] BuildV2Ipv4AddressBlock(
        IPAddress source,
        IPAddress destination,
        ushort sourcePort,
        ushort destinationPort
    )
    {
        using MemoryStream buffer = new();
        buffer.Write(source.GetAddressBytes());
        buffer.Write(destination.GetAddressBytes());

        byte[] srcPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(srcPort, sourcePort);
        buffer.Write(srcPort);

        byte[] dstPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(dstPort, destinationPort);
        buffer.Write(dstPort);

        return buffer.ToArray();
    }

    private static byte[] BuildV2Ipv6AddressBlock(
        IPAddress source,
        IPAddress destination,
        ushort sourcePort,
        ushort destinationPort
    )
    {
        using MemoryStream buffer = new();
        buffer.Write(source.GetAddressBytes());
        buffer.Write(destination.GetAddressBytes());

        byte[] srcPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(srcPort, sourcePort);
        buffer.Write(srcPort);

        byte[] dstPort = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(dstPort, destinationPort);
        buffer.Write(dstPort);

        return buffer.ToArray();
    }

    [Fact]
    public async Task V1_ATcp4Line_ParsesTheClaimedSourceAddress()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY TCP4 192.168.0.1 192.168.0.11 56324 443\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Equal(IPAddress.Parse("192.168.0.1"), result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V1_ATcp6Line_ParsesTheClaimedSourceAddress()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY TCP6 2001:db8::1 2001:db8::2 56324 443\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Equal(IPAddress.Parse("2001:db8::1"), result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V1_AnUnknownLine_IsValidButCarriesNoClaimedAddress()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY UNKNOWN\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Null(result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V1_AnUnknownLineWithPlaceholderAddresses_IsValidButCarriesNoClaimedAddress()
    {
        // Some proxies append placeholder address and port fields to an UNKNOWN line; this must
        // be accepted, not rejected for the token count.
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY UNKNOWN 0.0.0.0 0.0.0.0 0 0\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Null(result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V1_AFamilyAddressMismatch_IsRejected()
    {
        // TCP4 is claimed, but the address is an IPv6 literal.
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY TCP4 2001:db8::1 2001:db8::2 1 2\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V1_AGarbageLine_IsRejected()
    {
        byte[] bytes = Encoding.ASCII.GetBytes("NOT A PROXY LINE AT ALL\r\n");

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V1_AnInvalidPortNumber_IsRejected()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(
            "PROXY TCP4 192.168.0.1 192.168.0.11 not-a-port 443\r\n"
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V1_ALineOf107CharactersWithItsCrlf_IsAccepted()
    {
        // PROXY protocol section 2.1: the longest valid v1 line is 107 characters, CRLF included.
        // An UNKNOWN line may carry any text after the keyword, which makes a line of exact length.
        string line = "PROXY UNKNOWN ";
        line += new string('x', 105 - line.Length) + "\r\n";
        Assert.Equal(107, line.Length);

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(Encoding.ASCII.GetBytes(line)),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task V1_ALineOf108CharactersWithItsCrlf_IsRejected()
    {
        // One character past the specification's limit: the CRLF is not found in the first 107
        // characters, so the line is invalid.
        string line = "PROXY UNKNOWN ";
        line += new string('x', 106 - line.Length) + "\r\n";
        Assert.Equal(108, line.Length);

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(Encoding.ASCII.GetBytes(line)),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V1_ALineWithNoTerminatingCrlfWithinTheDefensiveMaximum_IsRejected()
    {
        // Well past the 107-byte maximum, with no CRLF anywhere.
        byte[] bytes = Encoding.ASCII.GetBytes("PROXY " + new string('X', 200));

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(bytes),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ATrulyEmptyConnection_IsRejected()
    {
        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf([]),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V2_ATcp4ProxyHeader_ParsesTheClaimedSourceAddress()
    {
        byte[] addressBlock = BuildV2Ipv4AddressBlock(
            IPAddress.Parse("192.0.2.5"),
            IPAddress.Parse("192.0.2.6"),
            56324,
            853
        );
        byte[] header = BuildV2Header(
            versionAndCommand: 0x21,
            familyAndProtocol: 0x11,
            addressBlock
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Equal(IPAddress.Parse("192.0.2.5"), result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V2_ATcp6ProxyHeader_ParsesTheClaimedSourceAddress()
    {
        byte[] addressBlock = BuildV2Ipv6AddressBlock(
            IPAddress.Parse("2001:db8::5"),
            IPAddress.Parse("2001:db8::6"),
            56324,
            853
        );
        byte[] header = BuildV2Header(
            versionAndCommand: 0x21,
            familyAndProtocol: 0x21,
            addressBlock
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Equal(IPAddress.Parse("2001:db8::5"), result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V2_ATcp4ProxyHeaderWithTrailingTlvBytes_StillParsesCorrectly()
    {
        byte[] addressBlock =
        [
            .. BuildV2Ipv4AddressBlock(
                IPAddress.Parse("192.0.2.5"),
                IPAddress.Parse("192.0.2.6"),
                1,
                2
            ),
            .. new byte[] { 0x01, 0x00, 0x02, 0xAA, 0xBB }, // an arbitrary, unparsed TLV.
        ];
        byte[] header = BuildV2Header(
            versionAndCommand: 0x21,
            familyAndProtocol: 0x11,
            addressBlock
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Equal(IPAddress.Parse("192.0.2.5"), result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V2_ALocalCommand_IsValidButCarriesNoClaimedAddress()
    {
        // Command 0x0 (LOCAL), a health check. The well-formed IPv4 address block that follows
        // must be ignored.
        byte[] addressBlock = BuildV2Ipv4AddressBlock(
            IPAddress.Parse("192.0.2.5"),
            IPAddress.Parse("192.0.2.6"),
            1,
            2
        );
        byte[] header = BuildV2Header(
            versionAndCommand: 0x20,
            familyAndProtocol: 0x11,
            addressBlock
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Null(result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V2_AnUnsupportedFamily_IsValidButCarriesNoClaimedAddress()
    {
        // 0x00 is AF_UNSPEC/UNSPEC: valid, but not a family this TCP listener can use.
        byte[] header = BuildV2Header(versionAndCommand: 0x21, familyAndProtocol: 0x00, []);

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.True(result.IsValid);
        Assert.Null(result.ClaimedSourceAddress);
    }

    [Fact]
    public async Task V2_AnUnsupportedVersion_IsRejected()
    {
        // High nibble 0x1 is version 1 in binary form, which is not understood.
        byte[] header = BuildV2Header(versionAndCommand: 0x11, familyAndProtocol: 0x11, []);

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V2_ATruncatedSignature_IsRejected()
    {
        byte[] header = [.. V2Signature.AsSpan(0, 8).ToArray()];

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task V2_AnAddressBlockTooShortForTcp4_IsRejected()
    {
        byte[] header = BuildV2Header(
            versionAndCommand: 0x21,
            familyAndProtocol: 0x11,
            [0x01, 0x02, 0x03]
        );

        ProxyProtocolParseResult result = await ProxyProtocolReader.ReadAsync(
            StreamOf(header),
            CancellationToken.None
        );

        Assert.False(result.IsValid);
    }
}
