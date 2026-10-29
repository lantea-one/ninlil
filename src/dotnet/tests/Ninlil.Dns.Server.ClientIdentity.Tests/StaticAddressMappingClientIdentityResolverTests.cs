using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests;

/// <summary>
///     This class tests <see cref="StaticAddressMappingClientIdentityResolver" />: exact-address
///     and CIDR mappings, disabled mappings, IPv4-mapped addresses, and that a resolved identity
///     finds its known client. It runs against <see cref="StaticAddressMappingTestHost" />, which
///     uses the Entity Framework repositories on a temporary SQLite database.
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
    ///     This test checks that an IPv4-mapped IPv6 source address ("::ffff:x.x.x.x"), as a
    ///     dual-stack listener reports an IPv4 client, still matches a mapping whose <c>Cidr</c> is
    ///     a plain IPv4 address or network.
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
    ///     This test checks that a resolved identity finds a known client: the
    ///     <c>(ClientIdentifier, ProtocolVersion)</c> pair of the <see cref="ClientIdentityResolution" />
    ///     finds a separately seeded <c>DnsClientEntity</c> through
    ///     <see cref="IDnsClientRepository" />, the registry that per-client features (list
    ///     assignments, schedules, pause, the activity view) are keyed on. That is what those
    ///     features depend on, so the test checks it directly rather than through the query
    ///     pipeline.
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

    /// <summary>
    ///     This test checks that queries are answered from the in-memory snapshot: 100 resolves
    ///     read the repository once.
    /// </summary>
    [Fact]
    public async Task ResolveAsync_ManyCalls_ReadTheRepositoryOnlyOnce()
    {
        await _host.SeedMappingAsync(
            "10.99.0.0/24",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        for (int i = 0; i < 100; i++)
            Assert.NotNull(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42")));

        Assert.Equal(1, _host.RepositoryCalls["ListEnabledAsync"]);
    }

    [Fact]
    public async Task ResolveAsync_AfterAMappingChanges_ShowsItOnceTheSnapshotIsRefreshed()
    {
        await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42"));
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );

        // The snapshot is not stale yet, so the new mapping is not visible.
        Assert.Null(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42")));

        await _host.Resolver.RefreshAsync();

        Assert.NotNull(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42")));
    }

    [Fact]
    public async Task ResolveAsync_AfterTheTimeToLive_RefreshesInTheBackgroundAndServesTheOldSnapshotMeanwhile()
    {
        await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42"));
        await _host.SeedMappingAsync(
            "10.99.0.42",
            "aa:bb:cc:dd:ee:ff",
            InternetProtocolVersionEnum.IPv4
        );
        _host.Time.Advance(StaticAddressMappingClientIdentityResolver.SnapshotTimeToLive);

        // The read that finds the snapshot stale still gets the old answer.
        Assert.Null(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42")));
        await _host.Resolver.WhenIdleAsync();

        Assert.NotNull(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.99.0.42")));
    }

    [Fact]
    public async Task ResolveAsync_AMappingWhoseCidrDoesNotParse_MatchesNothingAndTheOthersStillResolve()
    {
        await _host.SeedMappingAsync(
            "not a network",
            "aa:aa:aa:aa:aa:aa",
            InternetProtocolVersionEnum.IPv4
        );
        await _host.SeedMappingAsync(
            "10.77.0.0/99",
            "bb:bb:bb:bb:bb:bb",
            InternetProtocolVersionEnum.IPv4
        );
        await _host.SeedMappingAsync(
            "10.77.0.0/24",
            "cc:cc:cc:cc:cc:cc",
            InternetProtocolVersionEnum.IPv4
        );

        ClientIdentityResolution resolution = await _host.Resolver.ResolveAsync(
            IPAddress.Parse("10.77.0.5")
        );

        Assert.NotNull(resolution);
        Assert.Equal("cc:cc:cc:cc:cc:cc", resolution.ClientIdentifier);
        Assert.Null(await _host.Resolver.ResolveAsync(IPAddress.Parse("10.78.0.5")));
    }
}
