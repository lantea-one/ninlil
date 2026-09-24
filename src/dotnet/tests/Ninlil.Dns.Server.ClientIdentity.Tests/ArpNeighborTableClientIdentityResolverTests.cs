using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class proves <see cref="ArpNeighborTableClientIdentityResolver" /> genuinely reads
///     and resolves against this machine's own real, live <c>/proc/net/arp</c> file -- not a
///     synthetic substitute -- since this project's own working-style rule ("a passing
///     <c>dotnet build</c> is not evidence anything works") requires a real backing mechanism to
///     actually be exercised wherever the sandbox genuinely has one, and this sandbox confirmed
///     does (verified directly via <c>cat /proc/net/arp</c> before writing this test). This test
///     discovers its own expected answer from a fresh, independent read of the same real file
///     rather than hardcoding any specific address, so it keeps passing regardless of which
///     specific neighbors this machine happens to know about at test time.
/// </summary>
public sealed class ArpNeighborTableClientIdentityResolverTests
{
    [Fact]
    public async Task RefreshAsync_ThenResolveAsync_ResolvesARealEntryFromThisMachinesOwnArpTable()
    {
        // Read the real file ourselves, independently of the resolver under test, to discover
        // one real entry to assert against -- this is what makes this test self-verifying rather
        // than dependent on a specific IP happening to exist on whatever machine runs it.
        IReadOnlyList<ArpNeighborTableEntry> realEntries = ArpNeighborTableParser.Parse(
            await File.ReadAllTextAsync("/proc/net/arp")
        );

        // If this sandbox's own ARP table happens to be genuinely empty at the moment this test
        // runs, there is nothing left to assert against real content -- rather than failing (or
        // silently no-op'ing), fall back to proving the resolver's own "no known entries yet"
        // behavior is still correct and non-crashing.
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

        // Force a real, synchronous read of the real file rather than waiting on the background
        // timer loop.
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

        // TEST-NET-3 (RFC 5737) -- reserved for documentation, guaranteed never to appear on any
        // real network this sandbox could actually be attached to.
        ClientIdentityResolution resolution = await resolver.ResolveAsync(
            IPAddress.Parse("203.0.113.254")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_WithAnIPv6Address_AlwaysReturnsNull()
    {
        // ARP itself (RFC 826) only ever resolves IPv4 -- see this resolver's own doc comment.
        ArpNeighborTableClientIdentityResolver resolver = new(
            NullLogger<ArpNeighborTableClientIdentityResolver>.Instance
        );
        await resolver.RefreshAsync();

        ClientIdentityResolution resolution = await resolver.ResolveAsync(IPAddress.IPv6Loopback);

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This test is the regression proof for a real, previously-undiscovered bug 15c's own
    ///     live end-to-end verification found: this project's own Plain DNS UDP listener is
    ///     always a dual-stack socket bound to the IPv6 wildcard address, so a real IPv4 client's
    ///     own source address arrives at this resolver as an IPv4-mapped IPv6 address
    ///     ("::ffff:x.x.x.x", <see cref="System.Net.Sockets.AddressFamily.InterNetworkV6" />)
    ///     rather than a plain <see cref="System.Net.Sockets.AddressFamily.InterNetwork" /> one --
    ///     before this fix, the family check
    ///     inside <see cref="ArpNeighborTableClientIdentityResolver.ResolveAsync" /> silently
    ///     rejected every single real-world query's own address before ever reaching the
    ///     dictionary lookup, no matter how complete the ARP table itself was.
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
