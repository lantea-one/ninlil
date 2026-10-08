using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Protocol.Dhcpv6.Tests;

/// <summary>
///     This class is a test stand-in for the DHCPv6 server engine
///     (<c>Ninlil.Dhcp.Server.Core.Dhcpv6RequestHandler</c>), like
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests.StubDhcpv4RequestHandler</c>. It answers every
///     SOLICIT with a fixed ADVERTISE offering one address, so the transport tests can check
///     sockets and the codec without address assignment.
/// </summary>
internal sealed class StubDhcpv6RequestHandler : IDhcpv6RequestHandler
{
    /// <summary>
    ///     The address this stub always offers.
    /// </summary>
    public static readonly IPAddress OfferedAddress = IPAddress.Parse("2001:db8:1::100");

    /// <summary>
    ///     The server DUID this stub always sends.
    /// </summary>
    public static readonly byte[] ServerIdentifier = [0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD];

    /// <summary>
    ///     This property reports whether <see cref="HandleRequestAsync" /> was called, so a test
    ///     can check the listener passed the request on.
    /// </summary>
    public bool ReceivedRequest { get; private set; }

    /// <summary>
    ///     This property, when set, replaces the fixed ADVERTISE: <see cref="HandleRequestAsync" />
    ///     returns whatever the function returns (or lets it throw). A resilience test uses it to
    ///     make the handler misbehave on chosen requests.
    /// </summary>
    public Func<Dhcpv6Message, Dhcpv6Message> Override { get; set; }

    /// <inheritdoc />
    public ValueTask<Dhcpv6Message> HandleRequestAsync(
        Dhcpv6Message request,
        IPEndPoint remoteEndPoint,
        CancellationToken cancellationToken
    )
    {
        ReceivedRequest = true;

        if (Override is not null)
            return ValueTask.FromResult(Override(request));

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
