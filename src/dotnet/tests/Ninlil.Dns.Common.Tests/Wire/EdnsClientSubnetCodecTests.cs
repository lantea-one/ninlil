using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Common.Tests.Wire;

/// <summary>
///     This class tests <see cref="EdnsClientSubnetCodec" />, RFC 7871 §6's EDNS Client Subnet
///     option wire format.
/// </summary>
public class EdnsClientSubnetCodecTests
{
    [Fact]
    public void EncodeThenTryDecodeRoundTripsAnIPv4Address()
    {
        IPAddress address = IPAddress.Parse("203.0.113.7");

        DnsEdnsOption encoded = EdnsClientSubnetCodec.Encode(address, 24, scopePrefixLength: 0);

        Assert.True(EdnsClientSubnetCodec.TryDecode(encoded, out EdnsClientSubnetOptionData data));
        Assert.Equal(AddressFamily.InterNetwork, data.Family);
        Assert.Equal(IPAddress.Parse("203.0.113.0"), data.Address);
        Assert.Equal(24, data.SourcePrefixLength);
        Assert.Equal(0, data.ScopePrefixLength);
    }

    [Fact]
    public void EncodeThenTryDecodeRoundTripsAnIPv6Address()
    {
        IPAddress address = IPAddress.Parse("2001:db8:1234:5678::1");

        DnsEdnsOption encoded = EdnsClientSubnetCodec.Encode(address, 56, scopePrefixLength: 19);

        Assert.True(EdnsClientSubnetCodec.TryDecode(encoded, out EdnsClientSubnetOptionData data));
        Assert.Equal(AddressFamily.InterNetworkV6, data.Family);
        Assert.Equal(IPAddress.Parse("2001:db8:1234:5600::"), data.Address);
        Assert.Equal(56, data.SourcePrefixLength);
        Assert.Equal(19, data.ScopePrefixLength);
    }

    [Fact]
    public void EncodeSetsTheOptionCodeToClientSubnet()
    {
        DnsEdnsOption encoded = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("192.0.2.1"),
            32,
            scopePrefixLength: 0
        );

        Assert.Equal((ushort)EdnsOptionCodeEnum.ClientSubnet, encoded.Code);
    }

    [Theory]
    [InlineData("192.0.2.255", "192.0.2.128", (byte)25)]
    [InlineData("192.0.2.255", "192.0.0.0", (byte)16)]
    public void EncodeMasksBitsBeyondTheSourcePrefixLength(
        string input,
        string expectedMasked,
        byte sourcePrefixLength
    )
    {
        DnsEdnsOption encoded = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse(input),
            sourcePrefixLength,
            scopePrefixLength: 0
        );

        Assert.True(EdnsClientSubnetCodec.TryDecode(encoded, out EdnsClientSubnetOptionData data));
        Assert.Equal(IPAddress.Parse(expectedMasked), data.Address);
    }

    [Fact]
    public void EncodeOfTwoAddressesDifferingOnlyPastTheSourcePrefixLengthEncodesIdentically()
    {
        DnsEdnsOption first = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("198.51.100.1"),
            24,
            scopePrefixLength: 0
        );
        DnsEdnsOption second = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("198.51.100.254"),
            24,
            scopePrefixLength: 0
        );

        Assert.Equal(first.Data, second.Data);
    }

    [Fact]
    public void TryDecodeRejectsATruncatedPayload()
    {
        DnsEdnsOption tooShort = new((ushort)EdnsOptionCodeEnum.ClientSubnet, [0, 1, 24]);

        Assert.False(
            EdnsClientSubnetCodec.TryDecode(tooShort, out EdnsClientSubnetOptionData data)
        );
        Assert.Null(data);
    }

    [Fact]
    public void TryDecodeRejectsAnUnrecognizedFamily()
    {
        // Family 3 is not IPv4 (1) or IPv6 (2).
        DnsEdnsOption unknownFamily = new(
            (ushort)EdnsOptionCodeEnum.ClientSubnet,
            [0, 3, 24, 0, 192, 0, 2]
        );

        Assert.False(
            EdnsClientSubnetCodec.TryDecode(unknownFamily, out EdnsClientSubnetOptionData data)
        );
        Assert.Null(data);
    }

    [Fact]
    public void TryDecodeRejectsASourcePrefixLengthExceedingTheFamilysMaximum()
    {
        // Family 1 (IPv4) with a source prefix length of 33, one bit past its own 32-bit maximum.
        DnsEdnsOption overlong = new(
            (ushort)EdnsOptionCodeEnum.ClientSubnet,
            [0, 1, 33, 0, 192, 0, 2, 1, 0]
        );

        Assert.False(
            EdnsClientSubnetCodec.TryDecode(overlong, out EdnsClientSubnetOptionData data)
        );
        Assert.Null(data);
    }

    [Fact]
    public void TryDecodeRejectsAnAddressLengthThatDoesNotMatchTheSourcePrefixLength()
    {
        // A /24 needs exactly 3 address bytes; this payload carries 4.
        DnsEdnsOption mismatched = new(
            (ushort)EdnsOptionCodeEnum.ClientSubnet,
            [0, 1, 24, 0, 192, 0, 2, 1]
        );

        Assert.False(
            EdnsClientSubnetCodec.TryDecode(mismatched, out EdnsClientSubnetOptionData data)
        );
        Assert.Null(data);
    }

    [Fact]
    public void TryDecodeRejectsANullOption()
    {
        Assert.False(EdnsClientSubnetCodec.TryDecode(null, out EdnsClientSubnetOptionData data));
        Assert.Null(data);
    }
}
