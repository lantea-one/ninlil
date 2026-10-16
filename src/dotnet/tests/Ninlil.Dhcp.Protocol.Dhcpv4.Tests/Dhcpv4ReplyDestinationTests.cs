using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This test class checks <see cref="Dhcpv4UdpListener.ResolveReplyDestination" />, the pure
///     rule that picks where a DHCPv4 reply goes (RFC 2131 Section 4.1). It builds request and
///     reply messages in memory and needs no socket.
/// </summary>
public class Dhcpv4ReplyDestinationTests
{
    private const int ServerPort = 67;
    private const int ClientPort = 68;

    /// <summary>
    ///     This test covers the rule table: a relayed reply goes to the relay on the server port,
    ///     a NAK without a relay is always broadcast, and the other reply types follow the
    ///     broadcast flag and then "ciaddr".
    /// </summary>
    [Theory]
    [InlineData(Dhcpv4MessageTypeEnum.Nak, "0.0.0.0", "192.0.2.7", false, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Nak, "0.0.0.0", "0.0.0.0", false, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Nak, "0.0.0.0", "192.0.2.7", true, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Ack, "0.0.0.0", "192.0.2.7", false, "192.0.2.7", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Ack, "0.0.0.0", "192.0.2.7", true, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Offer, "0.0.0.0", "0.0.0.0", true, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Offer, "0.0.0.0", "0.0.0.0", false, "255.255.255.255", 68)]
    [InlineData(Dhcpv4MessageTypeEnum.Nak, "192.0.2.1", "192.0.2.7", false, "192.0.2.1", 67)]
    [InlineData(Dhcpv4MessageTypeEnum.Ack, "192.0.2.1", "0.0.0.0", false, "192.0.2.1", 67)]
    public void ResolveReplyDestination_ForTheRequestAndReply_PicksTheRfcDestination(
        Dhcpv4MessageTypeEnum replyType,
        string giaddr,
        string ciaddr,
        bool broadcastFlag,
        string expectedAddress,
        int expectedPort
    )
    {
        Dhcpv4Message request = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            RelayAgentIpAddress = IPAddress.Parse(giaddr),
            ClientIpAddress = IPAddress.Parse(ciaddr),
            BroadcastFlagSet = broadcastFlag,
        };
        Dhcpv4Message reply = new() { OperationCode = Dhcpv4OperationCodeEnum.BootReply };
        reply.SetMessageType(replyType);

        IPEndPoint destination = Dhcpv4UdpListener.ResolveReplyDestination(
            request,
            reply,
            ServerPort,
            ClientPort
        );

        Assert.Equal(new IPEndPoint(IPAddress.Parse(expectedAddress), expectedPort), destination);
    }
}
