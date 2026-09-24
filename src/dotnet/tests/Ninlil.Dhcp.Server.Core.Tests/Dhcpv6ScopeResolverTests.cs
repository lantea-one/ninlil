using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Enum;
using Ninlil.Dhcp.Server.Core.Scope;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test proves <see cref="Dhcpv6ScopeResolver" />'s own new relay-link-address subnet
///     containment resolution step (this sub-phase's own addition), driving
///     <see cref="IDhcpv6ScopeResolver.ResolveAsync" /> directly against a real, SQLite-backed
///     <see cref="Dhcpv6EngineTestHost" /> -- real repositories, not mocked collaborators.
///     <see cref="Dhcpv6RequestHandlerTests" /> already covers this resolver's own
///     reservation/lease resolution steps indirectly; this class is scoped specifically to the new
///     step this sub-phase introduces.
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
        // No reservation, no lease -- the only signal this request carries at all is its own
        // relay-link-address, which falls inside scope B's own /64 but nowhere near scope A's.
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
        // Matches neither configured scope's own /64, and with two scopes configured there is no
        // single-scope fallback to reach either -- a genuinely ambiguous request resolves nothing.
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:ffff::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ResolveAsync_RelayLinkAddressSet_ButAReservationAlreadyMatches_ReservationWins()
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
        // The relay-link-address points at the *other* scope's own subnet -- the reservation must
        // still win, since it is resolved before this new step, exactly mirroring
        // Dhcpv4ScopeResolver's own precedence.
        request.RelayLinkAddress = IPAddress.Parse("2001:db8:1111::1");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.NotNull(resolved);
        Assert.Equal(reservedScope.Id, resolved.Id);
    }

    /// <summary>
    ///     This method resolves an address-assignment scope for a request, in its own fresh
    ///     dependency injection scope, mirroring how the real engine itself always resolves one.
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
