using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Scope;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the order of checks in <see cref="Dhcpv4ScopeResolver" />: a relayed
///     request is served only from the scope containing its <c>giaddr</c> (and gets no scope when
///     none does), while an unrelayed one tries a reservation, then <c>ciaddr</c>, then a lease
///     row, then the single scope. It runs against a <see cref="Dhcpv4EngineTestHost" /> (SQLite
///     file, real repositories), seeding rows through the repositories.
/// </summary>
public sealed class Dhcpv4ScopeResolverTests : IAsyncLifetime
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
    public async Task ResolveAsync_RelayedClientWithALeaseInAnotherScope_ResolvesTheGiaddrScope()
    {
        DhcpScopeEntity scope1 = await _host.SeedScopeAsync(
            "rel-lease-1",
            "10.0.1.10",
            "10.0.1.20"
        );
        DhcpScopeEntity scope2 = await _host.SeedScopeAsync(
            "rel-lease-2",
            "10.0.2.10",
            "10.0.2.20"
        );
        await SeedLeaseAsync(scope1.Name, "aa:bb:cc:00:00:01", "10.0.1.10");

        DhcpScopeEntity resolved = await ResolveAsync(
            NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x01], giaddr: "10.0.2.1")
        );

        Assert.Equal(scope2.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_RelayedClientWithAReservationInAnotherScope_ResolvesTheGiaddrScope()
    {
        DhcpScopeEntity scope1 = await _host.SeedScopeAsync("rel-res-1", "10.1.1.10", "10.1.1.20");
        DhcpScopeEntity scope2 = await _host.SeedScopeAsync("rel-res-2", "10.1.2.10", "10.1.2.20");
        await _host.SeedReservationAsync(scope1.Id, "aa:bb:cc:00:00:02", "10.1.1.15");

        DhcpScopeEntity resolved = await ResolveAsync(
            NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x02], giaddr: "10.1.2.1")
        );

        Assert.Equal(scope2.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_RelayedFromAnAddressNoScopeCovers_WithOneScope_ResolvesNothing()
    {
        await _host.SeedScopeAsync("rel-only", "10.2.1.10", "10.2.1.20");

        DhcpScopeEntity resolved = await ResolveAsync(
            NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x03], giaddr: "10.2.9.1")
        );

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Discover_RelayedFromTheSecondScope_IsOfferedAnAddressOnThatSubnet()
    {
        DhcpScopeEntity scope1 = await _host.SeedScopeAsync("off-1", "10.3.1.10", "10.3.1.20");
        await _host.SeedScopeAsync("off-2", "10.3.2.10", "10.3.2.20");
        await SeedLeaseAsync(scope1.Name, "aa:bb:cc:00:00:04", "10.3.1.10");

        Dhcpv4Message discover = NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x04], giaddr: "10.3.2.1");
        discover.SetMessageType(Dhcpv4MessageTypeEnum.Discover);

        Dhcpv4Message offer = await _host.Handler.HandleRequestAsync(
            discover,
            RemoteEndPoint,
            CancellationToken.None
        );

        Assert.NotNull(offer);
        Assert.StartsWith("10.3.2.", offer.YourIpAddress.ToString());
    }

    [Fact]
    public async Task ResolveAsync_UnrelayedWithCiaddrInTheSecondScope_IgnoresAStaleRowInTheFirst()
    {
        DhcpScopeEntity scope1 = await _host.SeedScopeAsync("ci-1", "10.4.1.10", "10.4.1.20");
        DhcpScopeEntity scope2 = await _host.SeedScopeAsync("ci-2", "10.4.2.10", "10.4.2.20");
        await SeedLeaseAsync(
            scope1.Name,
            "aa:bb:cc:00:00:05",
            "10.4.1.10",
            LeaseStateEnum.Released
        );

        Dhcpv4Message request = NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x05], giaddr: null);
        request.ClientIpAddress = IPAddress.Parse("10.4.2.15");

        DhcpScopeEntity resolved = await ResolveAsync(request);

        Assert.Equal(scope2.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_UnrelayedWithAReservationInOneScopeAndALeaseInAnother_ResolvesTheReservation()
    {
        DhcpScopeEntity scope1 = await _host.SeedScopeAsync("mix-1", "10.5.1.10", "10.5.1.20");
        DhcpScopeEntity scope2 = await _host.SeedScopeAsync("mix-2", "10.5.2.10", "10.5.2.20");
        await SeedLeaseAsync(scope1.Name, "aa:bb:cc:00:00:06", "10.5.1.10");
        await _host.SeedReservationAsync(scope2.Id, "aa:bb:cc:00:00:06", "10.5.2.15");

        DhcpScopeEntity resolved = await ResolveAsync(
            NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x06], giaddr: null)
        );

        Assert.Equal(scope2.Id, resolved?.Id);
    }

    [Fact]
    public async Task ResolveAsync_UnrelayedWithOnlyALeaseRow_ResolvesTheLeaseScope()
    {
        await _host.SeedScopeAsync("row-1", "10.6.1.10", "10.6.1.20");
        DhcpScopeEntity scope2 = await _host.SeedScopeAsync("row-2", "10.6.2.10", "10.6.2.20");
        await SeedLeaseAsync(scope2.Name, "aa:bb:cc:00:00:07", "10.6.2.10");

        DhcpScopeEntity resolved = await ResolveAsync(
            NewMessage([0xaa, 0xbb, 0xcc, 0, 0, 0x07], giaddr: null)
        );

        Assert.Equal(scope2.Id, resolved?.Id);
    }

    /// <summary>
    ///     This method builds a message from a client hardware address and an optional relay
    ///     agent address.
    /// </summary>
    private static Dhcpv4Message NewMessage(byte[] clientHardwareAddress, string giaddr)
    {
        Dhcpv4Message message = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x7001,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
        if (giaddr is not null)
            message.RelayAgentIpAddress = IPAddress.Parse(giaddr);
        return message;
    }

    /// <summary>
    ///     This method resolves a scope in a new dependency injection scope, as the engine does.
    /// </summary>
    private async Task<DhcpScopeEntity> ResolveAsync(Dhcpv4Message request)
    {
        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        IDhcpv4ScopeResolver resolver =
            scope.ServiceProvider.GetRequiredService<IDhcpv4ScopeResolver>();
        return await resolver.ResolveAsync(request, CancellationToken.None);
    }

    /// <summary>
    ///     This method saves a lease row through the repository in a separate scope.
    /// </summary>
    private async Task SeedLeaseAsync(
        string scopeName,
        string clientIdentifier,
        string ipAddress,
        LeaseStateEnum state = LeaseStateEnum.Active
    )
    {
        await using AsyncServiceScope scope = _host.Host.Services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<ILeaseRepository>()
            .AddAsync(
                new LeaseEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    ProtocolVersion = InternetProtocolVersionEnum.IPv4,
                    ScopeName = scopeName,
                    ClientIdentifier = clientIdentifier,
                    IpAddress = ipAddress,
                    State = state,
                    LeaseObtainedAtUtc = DateTimeOffset.UtcNow,
                    LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                }
            );
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}
