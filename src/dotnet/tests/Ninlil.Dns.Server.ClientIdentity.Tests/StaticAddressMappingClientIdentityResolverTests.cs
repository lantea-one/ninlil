using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This test proves the real Phase 16h manual static-address-mapping resolver end to end:
///     driven directly against a real, SQLite-backed <see cref="StaticAddressMappingTestHost" />
///     -- real repositories, real <c>SaveChangesAsync</c> commits, real reads back from storage,
///     not mocked collaborators.
/// </summary>
public sealed class StaticAddressMappingClientIdentityResolverTests : IAsyncLifetime
{
    private StaticAddressMappingTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new StaticAddressMappingTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task ResolveAsync_ExactAddressMapping_ResolvesTheConfiguredIdentifier()
    {
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.42")
        );

        Assert.NotNull(resolution);
        Assert.Equal("aa:bb:cc:dd:ee:ff", resolution.ClientIdentifier);
        Assert.Equal(InternetProtocolVersionEnum.IPv4, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_ExactAddressMapping_DoesNotMatchADifferentAddress()
    {
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.43")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_CidrMapping_ResolvesAnAddressWithinTheBlock()
    {
        await _host.SeedMappingAsync(
            "10.99.0.0/24",
            "11:22:33:44:55:66",
            InternetProtocolVersionEnum.IPv4
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.200")
        );

        Assert.NotNull(resolution);
        Assert.Equal("11:22:33:44:55:66", resolution.ClientIdentifier);
    }

    [Fact]
    public async Task ResolveAsync_CidrMapping_DoesNotResolveAnAddressOutsideTheBlock()
    {
        await _host.SeedMappingAsync(
            "10.99.0.0/24",
            "11:22:33:44:55:66",
            InternetProtocolVersionEnum.IPv4
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.1.5")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_Ipv6CidrMapping_ResolvesAnAddressWithinTheBlock()
    {
        await _host.SeedMappingAsync(
            "2001:db8:99::/64",
            "00:01:00:01:aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv6
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("2001:db8:99::42")
        );

        Assert.NotNull(resolution);
        Assert.Equal(InternetProtocolVersionEnum.IPv6, resolution.ProtocolVersion);
    }

    [Fact]
    public async Task ResolveAsync_DisabledMapping_IsNeverConsulted()
    {
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4,
            isDisabled: true
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.42")
        );

        Assert.Null(resolution);
    }

    [Fact]
    public async Task ResolveAsync_WithNoMappingsConfiguredAtAll_ReturnsNull()
    {
        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.42")
        );

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This test is the regression proof for the exact shape of bug this project's own
    ///     Phase 15c fix addresses for the ARP resolver, applied here: this project's own Plain
    ///     DNS UDP listener is always a dual-stack socket bound to the IPv6 wildcard address, so a
    ///     real IPv4 client's own source address arrives at this resolver as an IPv4-mapped IPv6
    ///     address ("::ffff:x.x.x.x") rather than a plain IPv4 one -- an operator who configures a
    ///     mapping's own <c>Cidr</c> as a plain IPv4 address/network must still have it match a
    ///     real, dual-stack-arriving query.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_Ipv4MappedIPv6Address_ResolvesTheSameAsItsPlainIPv4Form()
    {
        await _host.SeedMappingAsync(
            "10.99.0.0/24",
            "11:22:33:44:55:66",
            InternetProtocolVersionEnum.IPv4
        );

        IPAddress mappedAddress = IPAddress.Parse("10.99.0.200").MapToIPv6();

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(mappedAddress);

        Assert.NotNull(resolution);
        Assert.Equal("11:22:33:44:55:66", resolution.ClientIdentifier);
    }

    [Fact]
    public async Task ResolveAsync_NullAddress_ReturnsNullRatherThanThrowing()
    {
        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(null);

        Assert.Null(resolution);
    }

    /// <summary>
    ///     This is the live, end-to-end proof that a resolved identity genuinely functions as a
    ///     known client, not merely as an opaque string: a real query from a manually-pinned
    ///     address resolves to a <see cref="ClientIdentityResolution" /> whose own
    ///     <c>(ClientIdentifier, ProtocolVersion)</c> pair matches -- by exact key, not by
    ///     coincidence -- a real, independently-seeded <c>DnsClientEntity</c> row read back through
    ///     <see cref="IDnsClientRepository" />, the exact known-client registry every per-client
    ///     feature (filter-list assignment, schedules, pause, the activity view) is itself keyed
    ///     against. This is chosen over driving the full DNS query pipeline because it proves the
    ///     one thing every downstream consumer actually depends on -- the resolved identifier is a
    ///     real, matchable key into the known-client registry -- without needing to also stand up
    ///     and assert through an entire unrelated feature (filtering) as a proxy for that same fact.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ThenLookupInKnownClientRegistry_FindsTheSameRealClient()
    {
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );
        await _host.SeedKnownClientAsync(
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4,
            "Guest VLAN printer"
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.99.0.42")
        );
        Assert.NotNull(resolution);

        Data.Abstractions.Entity.DnsClientEntity knownClient = await _host.FindKnownClientAsync(
            resolution.ClientIdentifier,
            resolution.ProtocolVersion
        );

        Assert.NotNull(knownClient);
        Assert.Equal("Guest VLAN printer", knownClient.DisplayName);
    }
}
