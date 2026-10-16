using System.Net;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the address-family rule of
///     <c>Dhcpv4OptionDeliveryService</c>: a stored scope whose gateway or DNS server list holds
///     an IPv6 entry (the API refuses such a scope, but older data may hold one) still gets a
///     reply that carries its IPv4 entries, instead of losing the whole reply to the writer's
///     exception. The DHCPv6 mirror is in <see cref="Dhcpv6OptionOverlayServiceTests" />. Each
///     test calls <see cref="Dhcpv4RequestHandler.HandleRequestAsync" /> on a
///     <see cref="Dhcpv4EngineTestHost" />.
/// </summary>
public sealed class DhcpOptionDeliveryFamilyTests : IAsyncLifetime
{
    private static readonly IPEndPoint RemoteEndPoint = new(IPAddress.Loopback, 68);

    private Dhcpv4EngineTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv4EngineTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task Discover_WithIPv6EntriesInTheStoredLists_OffersTheIPv4EntriesOnly()
    {
        await _host.SeedScopeAsync(
            "mixed-family-scope",
            "192.0.2.50",
            "192.0.2.52",
            gatewayAddresses: ["2001:db8::1", "192.0.2.1"],
            dnsServerAddresses: ["2001:db8::53", "192.0.2.53"]
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover(),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.True(offer.TryGetRouters(out IReadOnlyList<IPAddress> routers));
        Assert.Equal(IPAddress.Parse("192.0.2.1"), Assert.Single(routers));
        Assert.True(offer.TryGetDomainNameServers(out IReadOnlyList<IPAddress> dnsServers));
        Assert.Equal(IPAddress.Parse("192.0.2.53"), Assert.Single(dnsServers));
    }

    [Fact]
    public async Task Discover_WithOnlyIPv6EntriesInTheStoredLists_StillOffersAnAddressWithoutThoseOptions()
    {
        await _host.SeedScopeAsync(
            "ipv6-only-lists-scope",
            "192.0.2.60",
            "192.0.2.62",
            gatewayAddresses: ["2001:db8::1"],
            dnsServerAddresses: ["2001:db8::53"]
        );

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            NewDiscover(),
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.False(offer.TryGetRouters(out _));
        Assert.False(offer.TryGetDomainNameServers(out _));
    }

    /// <summary>
    ///     This method builds a DISCOVER from a fixed client hardware address.
    /// </summary>
    private static Dhcpv4Message NewDiscover()
    {
        byte[] hardwareAddress = [0x0c, 0x0c, 0x0c, 0x0c, 0x0c, 0x0c];
        Dhcpv4Message discover = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x3000,
            HardwareAddressLength = (byte)hardwareAddress.Length,
            ClientHardwareAddress = hardwareAddress,
        };
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        return discover;
    }
}
