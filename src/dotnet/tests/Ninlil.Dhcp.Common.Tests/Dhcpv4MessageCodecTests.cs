using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class checks that <see cref="Dhcpv4MessageWriter" /> and
///     <see cref="Dhcpv4MessageReader" /> round-trip a <see cref="Dhcpv4Message" />, in memory
///     with no socket, and that the reader rejects malformed input (a truncated buffer, a bad
///     magic cookie, a dangling or overlong option). Socket-level tests live in
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c>.
/// </summary>
public class Dhcpv4MessageCodecTests
{
    /// <summary>
    ///     This method builds a minimal DHCPDISCOVER that several tests start from.
    /// </summary>
    private static Dhcpv4Message BuildDiscover()
    {
        Dhcpv4Message message = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            HardwareAddressType = 1,
            HardwareAddressLength = 6,
            Hops = 0,
            TransactionId = 0x1A2B3C4D,
            SecondsElapsed = 3,
            BroadcastFlagSet = true,
            ClientHardwareAddress = [0x02, 0x11, 0x22, 0x33, 0x44, 0x55],
        };

        message.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        message.SetParameterRequestList([1, 3, 6, 51]);
        message.SetHostName("test-host");

        return message;
    }

    [Fact]
    public void RoundTrip_Discover_PreservesFixedHeaderFields()
    {
        Dhcpv4Message original = BuildDiscover();

        byte[] bytes = Dhcpv4MessageWriter.Write(original);

        Assert.True(
            Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out string failureReason)
        );
        Assert.Null(failureReason);
        Assert.Equal(Dhcpv4OperationCodeEnum.BootRequest, decoded.OperationCode);
        Assert.Equal(original.HardwareAddressType, decoded.HardwareAddressType);
        Assert.Equal(original.HardwareAddressLength, decoded.HardwareAddressLength);
        Assert.Equal(original.TransactionId, decoded.TransactionId);
        Assert.Equal(original.SecondsElapsed, decoded.SecondsElapsed);
        Assert.True(decoded.BroadcastFlagSet);
        Assert.Equal(original.ClientHardwareAddress, decoded.ClientHardwareAddress);
    }

    [Fact]
    public void RoundTrip_Discover_PreservesWellKnownOptions()
    {
        byte[] bytes = Dhcpv4MessageWriter.Write(BuildDiscover());

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out _));

        Assert.True(decoded.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Discover, messageType);

        Assert.True(
            decoded.TryGetParameterRequestList(out IReadOnlyList<byte> parameterRequestList)
        );
        Assert.Equal(new byte[] { 1, 3, 6, 51 }, parameterRequestList);

        Assert.True(decoded.TryGetHostName(out string hostName));
        Assert.Equal("test-host", hostName);
    }

    [Fact]
    public void RoundTrip_Offer_PreservesAddressAndListOptions()
    {
        Dhcpv4Message offer = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootReply,
            TransactionId = 42,
            YourIpAddress = IPAddress.Parse("203.0.113.50"),
            ClientHardwareAddress = [0x02, 0x11, 0x22, 0x33, 0x44, 0x55],
        };

        offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
        offer.SetServerIdentifier(IPAddress.Parse("203.0.113.1"));
        offer.SetSubnetMask(IPAddress.Parse("255.255.255.0"));
        offer.SetIpAddressLeaseTime(TimeSpan.FromHours(2));
        offer.SetRouters([IPAddress.Parse("203.0.113.254")]);
        offer.SetDomainNameServers([
            IPAddress.Parse("203.0.113.53"),
            IPAddress.Parse("198.51.100.53"),
        ]);

        byte[] bytes = Dhcpv4MessageWriter.Write(offer);

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out _));

        Assert.Equal(IPAddress.Parse("203.0.113.50"), decoded.YourIpAddress);
        Assert.True(decoded.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Offer, messageType);
        Assert.True(decoded.TryGetServerIdentifier(out IPAddress serverIdentifier));
        Assert.Equal(IPAddress.Parse("203.0.113.1"), serverIdentifier);
        Assert.True(decoded.TryGetSubnetMask(out IPAddress subnetMask));
        Assert.Equal(IPAddress.Parse("255.255.255.0"), subnetMask);
        Assert.True(decoded.TryGetIpAddressLeaseTime(out TimeSpan leaseTime));
        Assert.Equal(TimeSpan.FromHours(2), leaseTime);
        Assert.True(decoded.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal([IPAddress.Parse("203.0.113.254")], routers);
        Assert.True(
            decoded.TryGetDomainNameServers(out IReadOnlyList<IPAddress> domainNameServers)
        );
        Assert.Equal(
            [IPAddress.Parse("203.0.113.53"), IPAddress.Parse("198.51.100.53")],
            domainNameServers
        );
    }

    [Fact]
    public void RoundTrip_RequestedIpAddressAndClientIdentifier_ArePreserved()
    {
        Dhcpv4Message request = new() { OperationCode = Dhcpv4OperationCodeEnum.BootRequest };
        request.SetMessageType(Dhcpv4MessageTypeEnum.Request);
        request.SetRequestedIpAddress(IPAddress.Parse("203.0.113.50"));
        request.SetClientIdentifier([0x01, 0x02, 0x11, 0x22, 0x33, 0x44, 0x55]);

        byte[] bytes = Dhcpv4MessageWriter.Write(request);

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out _));
        Assert.True(decoded.TryGetRequestedIpAddress(out IPAddress requestedIpAddress));
        Assert.Equal(IPAddress.Parse("203.0.113.50"), requestedIpAddress);
        Assert.True(decoded.TryGetClientIdentifier(out byte[] clientIdentifier));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x11, 0x22, 0x33, 0x44, 0x55 }, clientIdentifier);
    }

    [Fact]
    public void Write_PadsAMessageWithNoOptionsUpToTheMinimumMessageLength()
    {
        Dhcpv4Message message = new() { OperationCode = Dhcpv4OperationCodeEnum.BootRequest };

        byte[] bytes = Dhcpv4MessageWriter.Write(message);

        Assert.True(bytes.Length >= Dhcpv4Constants.MinimumMessageLength);
    }

    [Fact]
    public void Write_HardwareAddressLongerThan16Bytes_Throws()
    {
        Dhcpv4Message message = new() { ClientHardwareAddress = new byte[17] };

        Assert.Throws<ArgumentException>(() => Dhcpv4MessageWriter.Write(message));
    }

    [Fact]
    public void Write_NonIPv4Address_Throws()
    {
        Dhcpv4Message message = new() { ClientIpAddress = IPAddress.Parse("::1") };

        Assert.Throws<ArgumentException>(() => Dhcpv4MessageWriter.Write(message));
    }

    [Fact]
    public void Read_ClientHardwareAddress_IsTrimmedToTheHardwareAddressLength()
    {
        Dhcpv4Message message = new()
        {
            HardwareAddressLength = 6,
            ClientHardwareAddress = [0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF],
        };

        byte[] bytes = Dhcpv4MessageWriter.Write(message);

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out _));
        Assert.Equal(6, decoded.ClientHardwareAddress.Length);
        Assert.Equal(message.ClientHardwareAddress, decoded.ClientHardwareAddress);
    }

    [Fact]
    public void TryRead_BufferTooShortForHeader_ReturnsFalseWithAReason()
    {
        byte[] tooShort = new byte[10];

        Assert.False(
            Dhcpv4MessageReader.TryRead(
                tooShort,
                out Dhcpv4Message message,
                out string failureReason
            )
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_WrongMagicCookie_ReturnsFalseWithAReason()
    {
        byte[] bytes = Dhcpv4MessageWriter.Write(BuildDiscover());
        // Corrupt one byte of the magic cookie, which sits right after the 236-byte fixed header.
        bytes[Dhcpv4Constants.FixedHeaderLength] ^= 0xFF;

        Assert.False(
            Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_OptionWithNoLengthByte_ReturnsFalseWithAReason()
    {
        // A valid header and cookie followed by one option tag with no length byte.
        byte[] header = new byte[Dhcpv4Constants.FixedHeaderLength];
        byte[] bytes =
        [
            .. header,
            .. Dhcpv4Constants.MagicCookie,
            (byte)Dhcpv4OptionTagEnum.SubnetMask,
        ];

        Assert.False(
            Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_OptionLengthRunsPastTheBuffer_ReturnsFalseWithAReason()
    {
        byte[] header = new byte[Dhcpv4Constants.FixedHeaderLength];
        // Tag 1 (Subnet Mask), claiming a 4-byte value, with only 1 byte following.
        byte[] bytes =
        [
            .. header,
            .. Dhcpv4Constants.MagicCookie,
            (byte)Dhcpv4OptionTagEnum.SubnetMask,
            4,
            0x00,
        ];

        Assert.False(
            Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message message, out string failureReason)
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_PadBytesBeforeAnOptionAndTrailingPaddingAfterEnd_AreNotExposedAsOptions()
    {
        byte[] header = new byte[Dhcpv4Constants.FixedHeaderLength];
        byte[] bytes =
        [
            .. header,
            .. Dhcpv4Constants.MagicCookie,
            (byte)Dhcpv4OptionTagEnum.Pad,
            (byte)Dhcpv4OptionTagEnum.Pad,
            (byte)Dhcpv4OptionTagEnum.MessageType,
            1,
            (byte)Dhcpv4MessageTypeEnum.Discover,
            (byte)Dhcpv4OptionTagEnum.End,
            0x00,
            0x00,
            0x00,
        ];

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message message, out _));
        Assert.Single(message.Options);
        Assert.True(message.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType));
        Assert.Equal(Dhcpv4MessageTypeEnum.Discover, messageType);
    }

    [Fact]
    public void RoundTrip_ServerHostNameAndBootFileName_AreDecodedUpToTheirNullTerminator()
    {
        Dhcpv4Message message = new()
        {
            ServerHostName = "boot-server",
            BootFileName = "pxelinux.0",
        };

        byte[] bytes = Dhcpv4MessageWriter.Write(message);

        Assert.True(Dhcpv4MessageReader.TryRead(bytes, out Dhcpv4Message decoded, out _));
        Assert.Equal("boot-server", decoded.ServerHostName);
        Assert.Equal("pxelinux.0", decoded.BootFileName);
    }

    [Fact]
    public void SetRelayAgentInformation_WithA256ByteId_Throws()
    {
        Dhcpv4Message message = BuildDiscover();

        Assert.Throws<ArgumentException>(() =>
            message.SetRelayAgentInformation(circuitId: new byte[256])
        );
        Assert.Throws<ArgumentException>(() =>
            message.SetRelayAgentInformation(remoteId: new byte[256])
        );
    }

    [Fact]
    public void SetRelayAgentInformation_WithIdsThatOverflowTheOption_Throws()
    {
        Dhcpv4Message message = BuildDiscover();

        // 2 + 200 + 2 + 60 is 264 bytes, over the 255-byte option limit.
        Assert.Throws<ArgumentException>(() =>
            message.SetRelayAgentInformation(new byte[200], new byte[60])
        );
    }

    [Fact]
    public void SetRelayAgentInformation_WithIdsThatExactlyFit_EncodesAndReadsBack()
    {
        Dhcpv4Message message = BuildDiscover();

        // 2 + 200 + 2 + 51 is exactly 255 bytes.
        message.SetRelayAgentInformation(new byte[200], new byte[51]);

        Assert.True(
            Dhcpv4MessageReader.TryRead(
                Dhcpv4MessageWriter.Write(message),
                out Dhcpv4Message decoded,
                out _
            )
        );
        Assert.True(decoded.TryGetRelayAgentCircuitId(out byte[] circuitId));
        Assert.Equal(200, circuitId.Length);
        Assert.True(decoded.TryGetRelayAgentRemoteId(out byte[] remoteId));
        Assert.Equal(51, remoteId.Length);
    }
}
