using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv4;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv4.Tests;

/// <summary>
///     This is a minimal, test-only stand-in for the real DHCPv4 server engine (a future
///     sub-phase's own job) -- it answers every DISCOVER with one fixed, canned OFFER. It exists
///     purely so <see cref="Dhcpv4UdpRoundTripTests" /> can prove the transport and wire codec
///     work correctly over a real socket, independent of any real lease-allocation logic.
/// </summary>
internal sealed class StubDhcpv4RequestHandler : IDhcpv4RequestHandler
{
    /// <summary>
    ///     The fixed address this stub always offers, regardless of what a real scope/pool would
    ///     actually have available.
    /// </summary>
    public static readonly IPAddress OfferedAddress = IPAddress.Parse("203.0.113.50");

    /// <summary>
    ///     The fixed address this stub always identifies itself as the server for.
    /// </summary>
    public static readonly IPAddress ServerIdentifier = IPAddress.Parse("203.0.113.1");

    /// <summary>
    ///     This property reports whether <see cref="HandleRequestAsync" /> was ever actually
    ///     invoked, so a test can assert the listener really did dispatch a decoded request to
    ///     this handler rather than merely appearing to succeed some other way.
    /// </summary>
    public bool ReceivedRequest { get; private set; }

    /// <summary>
    ///     This property captures the most recently received request's own client hardware
    ///     address, so a test can confirm it survived the real wire round trip unchanged.
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
