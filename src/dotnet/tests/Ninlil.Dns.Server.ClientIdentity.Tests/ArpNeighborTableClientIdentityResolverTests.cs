using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class tests <see cref="ArpNeighborTableClientIdentityResolver" /> against the test
///     machine's live <c>/proc/net/arp</c>, so it needs a Linux host. The expected answer comes
///     from reading the same file independently rather than from a hardcoded address, so the
///     tests pass whichever neighbors the machine knows. When the table is empty, the tests fall
///     back to weaker checks (see each test).
/// </summary>
public sealed class ArpNeighborTableClientIdentityResolverTests
{
    [Fact]
    public async Task RefreshAsync_ThenResolveAsync_ResolvesARealEntryFromThisMachinesOwnArpTable()
    {
        // Read the file independently of the resolver to find an entry to assert against, so
        // the test does not depend on a particular address existing on the machine.
        IReadOnlyList<ArpNeighborTableEntry> realEntries = ArpNeighborTableParser.Parse(
            await File.ReadAllTextAsync("/proc/net/arp")
        );

        // With an empty ARP table there is no entry to resolve, so check instead that the
        // resolver answers null without throwing.
        if (realEntries.Count == 0)
        {
            ArpNeighborTableClientIdentityResolver emptyTableResolver = new(
                NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
            );
            await emptyTableResolver.RefreshAsync();

            ClientIdentityResolution emptyResult = await emptyTableResolver.ResolveAsync(
                IPAddress.Parse("203.0.113.1")
            );

            Assert.Null(emptyResult);
            return;
        }

        ArpNeighborTableEntry realEntry = realEntries[0];

        ArpNeighborTableClientIdentityResolver resolver = new(
            NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
        );

        // Read the file now rather than waiting for the background loop.
        await resolver.RefreshAsync();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(realEntry.IpAddress);

        Assert.NotNull(resolution);
        Assert.Equal(realEntry.HardwareAddress, resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_WithAnAddressNeverSeenInTheArpTable_ReturnsNull()
    {
        ArpNeighborTableClientIdentityResolver resolver = new(
            NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
        );
        await resolver.RefreshAsync();

        // TEST-NET-3 (RFC 5737) is reserved for documentation, so it should not be in the ARP
        // table of a test machine.
        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("203.0.113.254")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_WithAnIPv6Address_AlwaysReturnsNull()
    {
        // ARP (RFC 826) resolves IPv4 only.
        ArpNeighborTableClientIdentityResolver resolver = new(
            NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
        );
        await resolver.RefreshAsync();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(IPAddress.IPv6Loopback);

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This test checks that an IPv4-mapped IPv6 address ("::ffff:x.x.x.x",
    ///     <see cref="System.Net.Sockets.AddressFamily.InterNetworkV6" />) resolves like its plain
    ///     IPv4 form. A dual-stack listener reports IPv4 clients that way, and without unmapping,
    ///     the IPv4 family check in <see cref="ArpNeighborTableClientIdentityResolver.ResolveAsync" />
    ///     would reject every such query. With an empty ARP table the test asserts nothing.
    /// </summary>
    [Fact]
    public async Task RefreshAsync_ThenResolveAsync_ResolvesAnIPv4MappedIPv6AddressTheSameAsItsPlainIPv4Form()
    {
        IReadOnlyList<ArpNeighborTableEntry> realEntries = ArpNeighborTableParser.Parse(
            await File.ReadAllTextAsync("/proc/net/arp")
        );

        if (realEntries.Count == 0)
            return;

        ArpNeighborTableEntry realEntry = realEntries[0];
        IPAddress mappedAddress = realEntry.IpAddress.MapToIPv6();

        ArpNeighborTableClientIdentityResolver resolver = new(
            NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
        );
        await resolver.RefreshAsync();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(mappedAddress);

        Assert.NotNull(resolution);
        Assert.Equal(realEntry.HardwareAddress, resolution.ClientIdentifier);
    }
}
