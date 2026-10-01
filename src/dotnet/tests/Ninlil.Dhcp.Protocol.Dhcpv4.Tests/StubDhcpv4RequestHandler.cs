using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This class is a test stand-in for the DHCPv4 server engine
///     (<c>Ninlil.Dhcp.Server.Core.Dhcpv4RequestHandler</c>). It answers every DISCOVER with a
///     fixed OFFER, so the transport tests can check sockets and the codec without lease
///     allocation.
/// </summary>
internal sealed class StubDhcpv4RequestHandler : IDhcpv4RequestHandler
{
    /// <summary>
    ///     The address this stub always offers.
    /// </summary>
    public static readonly IPAddress OfferedAddress = IPAddress.Parse("203.0.113.50");

    /// <summary>
    ///     The Server Identifier this stub always sends.
    /// </summary>
    public static readonly IPAddress ServerIdentifier = IPAddress.Parse("203.0.113.1");

    /// <summary>
    ///     This property reports whether <see cref="HandleRequestAsync" /> was called, so a test
    ///     can check the listener passed the request on.
    /// </summary>
    public bool ReceivedRequest { get; private set; }

    /// <summary>
    ///     This property holds the client hardware address of the last request, so a test can
    ///     check it survived the round trip.
    /// </summary>
    public byte[] LastRequestHardwareAddress { get; private set; }

    /// <inheritdoc />
    public ValueTask<Dhcpv4Message> HandleRequestAsync(
        Dhcpv4Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        ReceivedRequest = true;
        LastRequestHardwareAddress = request.ClientHardwareAddress;

        if (
            !request.TryGetMessageType(out Dhcpv4MessageTypeEnum messageType)
            || messageType != Dhcpv4MessageTypeEnum.Discover
        )
            return ValueTask.FromResult<Dhcpv4Message>(null);

        Dhcpv4Message offer = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootReply,
            TransactionId = request.TransactionId,
            YourIpAddress = OfferedAddress,
            ClientIpAddress = request.ClientIpAddress,
            RelayAgentIpAddress = request.RelayAgentIpAddress,
            BroadcastFlagSet = request.BroadcastFlagSet,
            ClientHardwareAddress = request.ClientHardwareAddress,
            HardwareAddressLength = request.HardwareAddressLength,
        };
        offer.SetMessageType(Dhcpv4MessageTypeEnum.Offer);
        offer.SetServerIdentifier(ServerIdentifier);
        offer.SetIpAddressLeaseTime(TimeSpan.FromHours(1));
        offer.SetSubnetMask(IPAddress.Parse("255.255.255.0"));

        return ValueTask.FromResult(offer);
    }
}
