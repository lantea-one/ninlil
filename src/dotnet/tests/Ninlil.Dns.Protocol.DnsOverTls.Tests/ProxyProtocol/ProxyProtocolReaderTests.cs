using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ninlil.Dns.Protocol.DnsOverTls.ProxyProtocol;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests.ProxyProtocol;

/// <summary>
///     This class covers <see cref="ProxyProtocolReader" /> directly against real, hand-built
///     PROXY protocol v1 (text) and v2 (binary) byte sequences, fed through a plain
///     <see cref="MemoryStream" /> -- no socket, listener, or TLS handshake needed, since this
///     class's own job is purely to decode bytes into a <see cref="ProxyProtocolParseResult" />.
///     The real end-to-end wiring (a genuine TCP connection, an allowlisted vs. non-allowlisted
///     source, the TLS handshake that follows) is covered separately in
///     <c>DnsOverTlsListenerTests</c>.
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
        // Some real proxy implementations still append placeholder address/port fields even for
        // an UNKNOWN line -- this must be tolerated, not rejected as a token-count mismatch.
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
        // TCP4 claimed, but the address itself is a real IPv6 literal.
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
    public async Task V1_ALineWithNoTerminatingCrlfWithinTheDefensiveMaximum_IsRejected()
    {
        // Well past the 108-byte defensive maximum, with no CRLF anywhere in it.
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
        // Command 0x0 (LOCAL) -- a health check. Even with a full, well-formed IPv4 address
        // block present, its contents must be ignored.
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
        // 0x00 == AF_UNSPEC/UNSPEC -- structurally valid, but not a family this TCP-only listener
        // can use.
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
        // High nibble 0x1 == version 1, which this project does not understand at all.
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
