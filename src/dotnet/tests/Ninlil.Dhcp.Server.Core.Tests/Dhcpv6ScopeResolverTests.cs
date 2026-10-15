using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Scope;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the relay link-address rule of <see cref="Dhcpv6ScopeResolver" />:
///     a relayed address request is served from the scope whose subnet contains the link address
///     (ahead of reservations and lease rows) and gets no scope when none does, a <c>::</c> link
///     address falls back to the lease row, and prefix delegation keeps its fallbacks. It calls <see cref="IDhcpv6ScopeResolver.ResolveAsync" /> on a
///     <see cref="Dhcpv6EngineTestHost" />. The reservation and lease steps are covered through
///     <see cref="Dhcpv6RequestHandlerTests" />.
/// </summary>
public sealed class Dhcpv6ScopeResolverTests : IAsyncLifetime
{
    private Dhcpv6EngineTestHost _host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _host = new Dhcpv6EngineTestHost();
        await _host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task ResolveAsync_RelayLinkAddressWithinOneOfSeveralScopesSubnet_ResolvesThatScope()
    {
        DhcpScopeEntity scopeA = await _host.SeedAddressScopeAsync(
            "relay-scope-a",
            "2001:db8:aaaa::10",
            "2001:db8:aaaa::20"
        );
        DhcpScopeEntity scopeB = await _host.SeedAddressScopeAsync(
            "relay-scope-b",
            "2001:db8:bbbb::10",
            "2001:db8:bbbb::20"
        );

        Dhcpv6Message request = new();
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06]);
        // No reservation or lease; the only hint is the relay link address, inside scope B's /64
        // and not scope A's.
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:bbbb::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.NotNull(resolved);
        Assert.Equal(scopeB.Id, resolved.Id);
        Assert.NotEqual(scopeA.Id, resolved.Id);
    }

    [Fact]
    public async Task ResolveAsync_RelayLinkAddressMatchingNoScope_WithSeveralScopesConfigured_ResolvesNothing()
    {
        await _host.SeedAddressScopeAsync(
            "relay-scope-c",
            "2001:db8:cccc::10",
            "2001:db8:cccc::20"
        );
        await _host.SeedAddressScopeAsync(
            "relay-scope-d",
            "2001:db8:dddd::10",
            "2001:db8:dddd::20"
        );

        Dhcpv6Message request = new();
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F]);
        // Matches neither scope's /64, and with two scopes there is no single-scope fallback, so
        // nothing resolves.
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:ffff::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_RelayedAddressRequestWithAReservationElsewhere_ResolvesTheLinkAddressScope()
    {
        DhcpScopeEntity reservedScope = await _host.SeedAddressScopeAsync(
            "relay-scope-reserved",
            "2001:db8:eeee::10",
            "2001:db8:eeee::20"
        );
        DhcpScopeEntity otherScope = await _host.SeedAddressScopeAsync(
            "relay-scope-other",
            "2001:db8:1111::10",
            "2001:db8:1111::20"
        );

        byte[] clientDuid = [0x00, 0x01, 0x00, 0x01, 0x99, 0x88, 0x77, 0x66, 0x55, 0x44];
        string clientKey = string.Join(':', clientDuid.Select(b => b.ToString("x2")));
        await _host.SeedReservationAsync(reservedScope.Id, clientKey, "2001:db8:eeee::15");

        Dhcpv6Message request = new();
        request.SetClientIdentifier(clientDuid);
        // The client sits behind a relay on the other scope's link, so that scope serves it even
        // though a reservation exists elsewhere (RFC 8415 Section 13.1).
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:1111::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Equal(otherScope.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_RelayedAddressRequestWithALeaseInAnotherScope_ResolvesTheLinkAddressScope()
    {
        DhcpScopeEntity scopeA = await _host.SeedAddressScopeAsync(
            "relay-lease-a",
            "2001:db8:a1a1::10",
            "2001:db8:a1a1::20"
        );
        DhcpScopeEntity scopeB = await _host.SeedAddressScopeAsync(
            "relay-lease-b",
            "2001:db8:b1b1::10",
            "2001:db8:b1b1::20"
        );

        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        await SeedLeaseAsync(scopeA.Name, duid, "2001:db8:a1a1::10");

        Dhcpv6Message request = new();
        request.SetClientIdentifier(duid);
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:b1b1::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Equal(scopeB.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_UnspecifiedLinkAddress_FallsBackToTheLeaseRow()
    {
        DhcpScopeEntity scopeA = await _host.SeedAddressScopeAsync(
            "relay-any-a",
            "2001:db8:a2a2::10",
            "2001:db8:a2a2::20"
        );
        await _host.SeedAddressScopeAsync("relay-any-b", "2001:db8:b2b2::10", "2001:db8:b2b2::20");

        byte[] duid = [0x00, 0x01, 0x00, 0x01, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26];
        await SeedLeaseAsync(scopeA.Name, duid, "2001:db8:a2a2::10");

        Dhcpv6Message request = new();
        request.SetClientIdentifier(duid);
        // A relay may send :: and identify the link by Interface-ID only (RFC 8415 Section 19.1.1).
        request.RelayLinkAddress = IPAddress.IPv6Any;

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Equal(scopeA.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_PrefixDelegationWithALinkAddressOutsideTheOnlyScope_ResolvesThatScope()
    {
        DhcpScopeEntity pdScope = await _host.SeedPrefixDelegationScopeAsync(
            "relay-pd",
            "2001:db8:9000::",
            "2001:db8:9fff::",
            56
        );

        Dhcpv6Message request = new();
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36]);
        // A delegated pool is not on the requesting router's link, so a link address outside it
        // still resolves the only prefix-delegation scope.
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:ffff::1");

        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        DhcpScopeEntity resolved = await scope
            .ServiceProvider.GetRequiredService<IDhcpv6ScopeResolver>()
            .ResolveAsync(request, DhcpScopeKindEnum.PrefixDelegation, CancellationToken.None);

        Assert.Equal(pdScope.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_RelayedAddressRequestWithOneScopeAndAnotherLink_ResolvesNothing()
    {
        await _host.SeedAddressScopeAsync("relay-solo", "2001:db8:5555::10", "2001:db8:5555::20");

        Dhcpv6Message request = new();
        request.SetClientIdentifier([0x00, 0x01, 0x00, 0x01, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46]);
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:6666::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Null(resolved);
    }

    /// <summary>
    ///     This method saves a lease row for a DUID through the repository in a separate scope.
    /// </summary>
    private async Task SeedLeaseAsync(string scopeName, byte[] duid, string ipAddress)
    {
        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv6,
                    ScopeName = scopeName,
                    ClientIdentifier = string.Join(':', duid.Select(b => b.ToString("x2"))),
                    IpAddress = ipAddress,
                    State = LeaseStateEnum.Active,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }

    /// <summary>
    ///     This method resolves an address-assignment scope for a request in a new dependency
    ///     injection scope, as the engine does.
    /// </summary>
    private async Task<DhcpScopeEntity> ResolveAsync(Dhcpv6Message request)
    {
        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        IDhcpv6ScopeResolver resolver =
            scope.ServiceProvider.GetRequiredService<IDhcpv6ScopeResolver>();
        return await resolver.ResolveAsync(
            request,
            DhcpScopeKindEnum.AddressAssignment,
            CancellationToken.None
        );
    }
}
