using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Tests;

/// <summary>
///     This test class checks that <see cref="Dhcpv6RelayMessageWriter" /> and
///     <see cref="Dhcpv6RelayMessageReader" /> round-trip a <see cref="Dhcpv6RelayMessage" />, in
///     memory with no socket, and that the reader rejects client/server messages, a missing Relay
///     Message option and a chain of relays. It is the relay counterpart of
///     <see cref="Dhcpv6MessageCodecTests" />; the code that uses the codec is tested in
///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Tests</c>, <c>Ninlil.Dhcp.Server.Core.Tests</c> and
///     <c>Ninlil.Dhcp.Server.Relay.Tests</c>.
/// </summary>
public class Dhcpv6RelayMessageCodecTests
{
    private static readonly IPAddress LinkAddress = IPAddress.Parse("2001:db8:1::1");
    private static readonly IPAddress PeerAddress = IPAddress.Parse("fe80::1234");

    private static Dhcpv6Message BuildInnerSolicit()
    {
        Dhcpv6Message message = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            TransactionId = 0xABCDEF,
        };
        message.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66]);
        return message;
    }

    private static Dhcpv6RelayMessage BuildRelayForward(Dhcpv6Message innerMessage)
    {
        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            HopCount = 0,
            LinkAddress = LinkAddress,
            PeerAddress = PeerAddress,
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerMessage));
        return relayForward;
    }

    [Fact]
    public void RoundTrip_RelayForward_PreservesHeaderAndNestedMessage()
    {
        Dhcpv6Message innerSolicit = BuildInnerSolicit();
        Dhcpv6RelayMessage original = BuildRelayForward(innerSolicit);
        original.SetInterfaceId([0x65, 0x74, 0x68, 0x30]); // "eth0"

        byte[] bytes = Dhcpv6RelayMessageWriter.Write(original);

        Assert.True(
            Dhcpv6RelayMessageReader.TryRead(
                bytes,
                out Dhcpv6RelayMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(failureReason);
        Assert.Equal(Dhcpv6MessageTypeEnum.RelayForward, decoded.MessageType);
        Assert.Equal((byte)0, decoded.HopCount);
        Assert.Equal(LinkAddress, decoded.LinkAddress);
        Assert.Equal(PeerAddress, decoded.PeerAddress);

        Assert.True(decoded.TryGetInterfaceId(out byte[] interfaceId));
        Assert.Equal([0x65, 0x74, 0x68, 0x30], interfaceId);

        Assert.True(decoded.TryGetRelayMessagePayload(out byte[] nestedPayload));
        Assert.True(
            Dhcpv6MessageReader.TryRead(
                nestedPayload,
                out Dhcpv6Message decodedInner,
                out string innerFailureReason
            )
        );
        Assert.Null(innerFailureReason);
        Assert.Equal(Dhcpv6MessageTypeEnum.Solicit, decodedInner.MessageType);
        Assert.Equal(innerSolicit.TransactionId, decodedInner.TransactionId);
        Assert.True(decodedInner.TryGetClientIdentifier(out byte[] duid));
        Assert.True(innerSolicit.TryGetClientIdentifier(out byte[] originalDuid));
        Assert.Equal(originalDuid, duid);
    }

    [Fact]
    public void RoundTrip_RelayReplyWithHopCount_PreservesHopCount()
    {
        Dhcpv6Message innerReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Reply,
            TransactionId = 0x1111,
        };
        innerReply.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            HopCount = 3,
            LinkAddress = LinkAddress,
            PeerAddress = PeerAddress,
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerReply));

        byte[] bytes = Dhcpv6RelayMessageWriter.Write(relayReply);

        Assert.True(
            Dhcpv6RelayMessageReader.TryRead(
                bytes,
                out Dhcpv6RelayMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(failureReason);
        Assert.Equal(Dhcpv6MessageTypeEnum.RelayReply, decoded.MessageType);
        Assert.Equal((byte)3, decoded.HopCount);
    }

    [Fact]
    public void Write_OrdinaryMessageType_Throws()
    {
        Dhcpv6RelayMessage message = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Solicit,
            LinkAddress = LinkAddress,
            PeerAddress = PeerAddress,
        };
        message.SetRelayMessagePayload([0x01]);

        Assert.Throws<ArgumentException>(() => Dhcpv6RelayMessageWriter.Write(message));
    }

    [Fact]
    public void Write_MissingLinkAddress_Throws()
    {
        Dhcpv6RelayMessage message = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            PeerAddress = PeerAddress,
        };
        message.SetRelayMessagePayload([0x01]);

        Assert.Throws<ArgumentException>(() => Dhcpv6RelayMessageWriter.Write(message));
    }

    [Fact]
    public void Write_Ipv4LinkAddress_Throws()
    {
        Dhcpv6RelayMessage message = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            LinkAddress = IPAddress.Parse("192.0.2.1"),
            PeerAddress = PeerAddress,
        };
        message.SetRelayMessagePayload([0x01]);

        Assert.Throws<ArgumentException>(() => Dhcpv6RelayMessageWriter.Write(message));
    }

    [Fact]
    public void TryRead_BufferTooShortForHeader_ReturnsFalseWithAReason()
    {
        byte[] tooShort = new byte[10];

        Assert.False(
            Dhcpv6RelayMessageReader.TryRead(
                tooShort,
                out Dhcpv6RelayMessage message,
                out string failureReason
            )
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_OrdinaryClientServerMessageType_ReturnsFalseWithAReason()
    {
        // A valid SOLICIT is not a relay message, so the relay reader must reject it.
        byte[] bytes = Dhcpv6MessageWriter.Write(BuildInnerSolicit());

        Assert.False(
            Dhcpv6RelayMessageReader.TryRead(
                bytes,
                out Dhcpv6RelayMessage message,
                out string failureReason
            )
        );
        Assert.Null(message);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_MissingRelayMessageOption_ReturnsFalseWithAReason()
    {
        // A valid 34-byte RELAY-FORW header with no options, so no Relay Message option, which
        // RFC 8415 Section 21.10 makes mandatory. It is built by hand because the code never
        // produces such a message through the writer.
        byte[] bytes = new byte[34];
        bytes[0] = (byte)Dhcpv6MessageTypeEnum.RelayForward;
        LinkAddress.GetAddressBytes().CopyTo(bytes, 2);
        PeerAddress.GetAddressBytes().CopyTo(bytes, 18);

        Assert.False(
            Dhcpv6RelayMessageReader.TryRead(
                bytes,
                out Dhcpv6RelayMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryRead_NestedRelayForwardWithinRelayForward_ReturnsFalseWithAReason()
    {
        // A chain of relays: the Relay Message payload is itself a RELAY-FORW. The codec supports
        // one hop only (see Dhcpv6RelayMessage) and must reject this rather than misread the
        // nested header as a client/server message.
        Dhcpv6RelayMessage innerRelayForward = BuildRelayForward(BuildInnerSolicit());
        byte[] innerRelayForwardBytes = Dhcpv6RelayMessageWriter.Write(innerRelayForward);

        Dhcpv6RelayMessage outerRelayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            HopCount = 1,
            LinkAddress = LinkAddress,
            PeerAddress = PeerAddress,
        };
        outerRelayForward.SetRelayMessagePayload(innerRelayForwardBytes);

        byte[] bytes = Dhcpv6RelayMessageWriter.Write(outerRelayForward);

        Assert.False(
            Dhcpv6RelayMessageReader.TryRead(
                bytes,
                out Dhcpv6RelayMessage decoded,
                out string failureReason
            )
        );
        Assert.Null(decoded);
        Assert.NotNull(failureReason);
    }

    [Fact]
    public void TryGetInterfaceId_NotSet_ReturnsFalse()
    {
        Dhcpv6RelayMessage message = BuildRelayForward(BuildInnerSolicit());

        Assert.False(message.TryGetInterfaceId(out byte[] interfaceId));
        Assert.Null(interfaceId);
    }

    [Fact]
    public void RemoteId_RoundTrip_PreservesEnterpriseNumberAndRemoteIdTogether()
    {
        Dhcpv6RelayMessage original = BuildRelayForward(BuildInnerSolicit());
        byte[] remoteId = [0x00, 0x00, 0x7E, 0xD9, 0xAA, 0xBB];
        original.SetRemoteId(remoteId);

        byte[] bytes = Dhcpv6RelayMessageWriter.Write(original);

        Assert.True(Dhcpv6RelayMessageReader.TryRead(bytes, out Dhcpv6RelayMessage decoded, out _));
        Assert.True(decoded.TryGetRemoteId(out byte[] decodedRemoteId));
        Assert.Equal(remoteId, decodedRemoteId);
    }

    [Fact]
    public void TryGetRemoteId_NotSet_ReturnsFalse()
    {
        Dhcpv6RelayMessage message = BuildRelayForward(BuildInnerSolicit());

        Assert.False(message.TryGetRemoteId(out byte[] remoteId));
        Assert.Null(remoteId);
    }

    [Fact]
    public void TryGetRemoteId_TooShortForEnterpriseNumber_ReturnsFalse()
    {
        Dhcpv6RelayMessage message = BuildRelayForward(BuildInnerSolicit());
        message.Options.Add(
            new Dhcpv6Option((ushort)Dhcpv6OptionCodeEnum.RemoteId, [0x00, 0x00, 0x7E])
        );

        Assert.False(message.TryGetRemoteId(out byte[] remoteId));
        Assert.Null(remoteId);
    }

    [Fact]
    public void SetRemoteId_TooShort_Throws()
    {
        Dhcpv6RelayMessage message = BuildRelayForward(BuildInnerSolicit());

        Assert.Throws<ArgumentException>(() => message.SetRemoteId([0x00, 0x00, 0x7E]));
    }
}
