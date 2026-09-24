using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Protocol.Dhcpv6;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay.Tests;

/// <summary>
///     This test proves <see cref="Dhcpv6RelayAgent" />'s own edge-case/defensive behavior
///     directly, by invoking its real <see cref="Dhcpv6RelayAgent.HandleRequestAsync" />/
///     <see cref="Dhcpv6RelayAgent.HandleRelayMessageAsync" /> methods without a real socket pair
///     -- edge/branch coverage this project's own separate, real, full round trip
///     (<see cref="Dhcpv6RelayRoundTripTests" />) deliberately does not repeat, the DHCPv6 analogue
///     of <see cref="Dhcpv4RelayAgentTests" />.
/// </summary>
public sealed class Dhcpv6RelayAgentTests
{
    [Fact]
    public async Task HandleRequestAsync_WithNoUpstreamServersConfigured_IsDroppedRatherThanThrowing()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: []);

        Dhcpv6Message request = new() { MessageType = Dhcpv6MessageTypeEnum.Solicit };
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);

        // Simply must not throw -- there is nowhere sensible to forward this to.
        Dhcpv6Message reply = await agent.HandleRequestAsync(
            request,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_ARelayForwardArrivingHere_IsIgnored()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: ["::1"]);

        // This relay's own dedicated outbound socket only ever legitimately receives a RELAY-REPL
        // back from an upstream server it forwarded to -- a RELAY-FORW reaching it would mean
        // something else entirely was sent there.
        Dhcpv6Message innerMessage = new() { MessageType = Dhcpv6MessageTypeEnum.Solicit };
        innerMessage.SetClientIdentifier([
            0x00,
            0x01,
            0x00,
            0x01,
            0x0A,
            0x0B,
            0x0C,
            0x0D,
            0x0E,
            0x0F,
        ]);

        Dhcpv6RelayMessage relayForward = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayForward,
            LinkAddress = IPAddress.Parse("2001:db8:1::1"),
            PeerAddress = IPAddress.Parse("fe80::1"),
        };
        relayForward.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerMessage));

        Dhcpv6RelayMessage reply = await agent.HandleRelayMessageAsync(
            relayForward,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    [Fact]
    public async Task HandleRelayMessageAsync_HopCountAboveDefensiveLimit_IsDiscarded()
    {
        using Dhcpv6RelayAgent agent = CreateAgent(upstreamServerAddresses: ["::1"]);

        Dhcpv6Message innerReply = new() { MessageType = Dhcpv6MessageTypeEnum.Reply };
        innerReply.SetServerIdentifier([0x00, 0x02, 0x00, 0x00, 0xAB, 0xCD]);

        Dhcpv6RelayMessage relayReply = new()
        {
            MessageType = Dhcpv6MessageTypeEnum.RelayReply,
            // This project's own defensive loop-prevention limit (Dhcpv6RelayAgent.MaxHops).
            HopCount = 17,
            LinkAddress = IPAddress.Parse("2001:db8:1::1"),
            PeerAddress = IPAddress.Parse("fe80::1"),
        };
        relayReply.SetRelayMessagePayload(Dhcpv6MessageWriter.Write(innerReply));

        Dhcpv6RelayMessage reply = await agent.HandleRelayMessageAsync(
            relayReply,
            new IPEndPoint(IPAddress.IPv6Loopback, 0),
            CancellationToken.None
        );

        Assert.Null(reply);
    }

    /// <summary>
    ///     This method builds a real <see cref="Dhcpv6RelayAgent" /> against a fixed, deterministic
    ///     link address and a caller-chosen upstream server list -- avoiding this test project's
    ///     own dependency injection registration entirely, since this class's own constructor
    ///     dependencies are cheap enough to construct directly by hand, the same convention
    ///     <c>Dhcpv4RelayAgentTests.CreateAgent</c> already establishes.
    /// </summary>
    private static Dhcpv6RelayAgent CreateAgent(IReadOnlyList<string> upstreamServerAddresses) =>
        new(
            Options.Create(
                new Dhcpv6RelayOptions
                {
                    Enable = true,
                    UpstreamServerAddresses = [.. upstreamServerAddresses],
                    UpstreamServerPort = 547,
                }
            ),
            Options.Create(new Dhcpv6ProtocolOptions()),
            new FixedDhcpv6RelayAgentAddressProvider(IPAddress.Parse("2001:db8:1::1")),
            NullLogger<Dhcpv6RelayAgent>.Instance
        );
}
