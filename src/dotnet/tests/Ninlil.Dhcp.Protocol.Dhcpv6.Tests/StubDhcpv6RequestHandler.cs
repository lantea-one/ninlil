using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This is a minimal, test-only stand-in for the real DHCPv6 server engine (a future
///     sub-phase's own job) -- it answers every SOLICIT with one fixed, canned ADVERTISE carrying
///     a single offered address. It exists purely so <see cref="Dhcpv6UdpRoundTripTests" /> can
///     prove the transport and wire codec work correctly over a real socket, independent of any
///     real address-assignment logic -- the DHCPv6 analogue of
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.StubDhcpv4RequestHandler</c>.
/// </summary>
internal sealed class StubDhcpv6RequestHandler : IDhcpv6RequestHandler
{
    /// <summary>
    ///     The fixed address this stub always offers, regardless of what a real scope/pool would
    ///     actually have available.
    /// </summary>
    public static readonly IPAddress OfferedAddress = IPAddress.Parse("2001:db8:1::100");

    /// <summary>
    ///     The fixed DUID bytes this stub always identifies itself as the server with.
    /// </summary>
    public static readonly byte[] ServerIdentifier = [0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD];

    /// <summary>
    ///     This property reports whether <see cref="HandleRequestAsync" /> was ever actually
    ///     invoked, so a test can assert the listener really did dispatch a decoded request to
    ///     this handler rather than merely appearing to succeed some other way.
    /// </summary>
    public bool ReceivedRequest { get; private set; }

    /// <inheritdoc />
    public ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        ReceivedRequest = true;

        if (request.MessageType != Dhcpv6MessageTypeEnum.Solicit)
            return ValueTask.FromResult<Dhcpv6Message>(null);

        Dhcpv6Message advertise = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.Advertise,
            TransactionId = request.TransactionId,
        };
        advertise.SetServerIdentifier(ServerIdentifier);

        if (request.TryGetClientIdentifier(out byte[] clientDuid))
            advertise.SetClientIdentifier(clientDuid);

        Dhcpv6IdentityAssociationForNonTemporaryAddresses association = new()
        {
            IdentityAssociationId = 1,
            T1 = TimeSpan.FromMinutes(30),
            T2 = TimeSpan.FromMinutes(48),
        };
        association.Addresses.Add(
            new Dhcpv6IdentityAssociationAddress
            {
                Address = OfferedAddress,
                PreferredLifetime = TimeSpan.FromHours(1),
                ValidLifetime = TimeSpan.FromHours(2),
            }
        );
        advertise.AddIdentityAssociationForNonTemporaryAddresses(association);

        return ValueTask.FromResult(advertise);
    }
}
