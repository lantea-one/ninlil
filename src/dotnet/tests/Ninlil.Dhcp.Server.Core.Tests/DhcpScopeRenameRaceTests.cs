using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Enum;
using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dhcp.Server.Core.Allocation;
using Ninlil.Dhcp.Server.Core.Identity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Tests;

/// <summary>
///     This test class covers the engine half of the scope rename race: a lease the allocation
///     services write under a stale scope name ends up under the scope's current name. Each test
///     builds the service by hand over a <see cref="ILeaseRepository" /> proxy whose first
///     <c>ListByScopeNameAsync</c> for the old name returns the stale result and then commits a
///     rename (scope and leases) through a separate dependency injection scope, as
///     <c>DhcpScopesController</c> does. The real repositories run on the SQLite file of the
///     engine test hosts, with the unique indexes enforced.
/// </summary>
public sealed class DhcpScopeRenameRaceTests : IAsyncLifetime
{
    private Dhcpv4EngineTestHost _v4Host;
    private Dhcpv6EngineTestHost _v6Host;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        _v4Host = new Dhcpv4EngineTestHost();
        await _v4Host.InitializeAsync();
        _v6Host = new Dhcpv6EngineTestHost();
        await _v6Host.InitializeAsync();
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _v4Host.DisposeAsync();
        await _v6Host.DisposeAsync();
    }

    [Fact]
    public async Task OfferV4_WhenTheScopeIsRenamedDuringTheRequest_StoresTheRowUnderTheNewName()
    {
        DhcpScopeEntity scope = await _v4Host.SeedScopeAsync(
            "rename-race-v4-offer",
            "192.0.2.10",
            "192.0.2.20"
        );
        Dhcpv4Message request = NewV4Message([0xb1, 0, 0, 0, 0, 1]);

        await using AsyncServiceScope di = _v4Host.Host.Services.CreateAsyncScope();
        bool fired = false;
        Dhcpv4LeaseAllocationService service = BuildV4Service(
            di.ServiceProvider,
            _v4Host.Host.Services,
            scope.Name,
            "rename-race-v4-offer-new",
            () => fired = true
        );

        LeaseEntity lease = await service.OfferAsync(scope, request);

        Assert.True(fired);
        Assert.NotNull(lease);
        await AssertOnlyUnderAsync(
            _v4Host.Host.Services,
            "rename-race-v4-offer-new",
            scope.Name,
            lease.ClientIdentifier
        );
    }

    [Fact]
    public async Task ConfirmV4_WhenTheScopeIsRenamedDuringTheRequest_DoesNotWriteTheOldNameBack()
    {
        DhcpScopeEntity scope = await _v4Host.SeedScopeAsync(
            "rename-race-v4-confirm",
            "192.0.2.30",
            "192.0.2.40"
        );
        Dhcpv4Message request = NewV4Message([0xb1, 0, 0, 0, 0, 2]);

        // The OFFER runs without a race and leaves the client's row under the old name.
        await using (AsyncServiceScope offerScope = _v4Host.Host.Services.CreateAsyncScope())
        {
            Dhcpv4LeaseAllocationService offerService = BuildV4Service(
                offerScope.ServiceProvider,
                _v4Host.Host.Services,
                scope.Name,
                null,
                () => { }
            );
            Assert.NotNull(await offerService.OfferAsync(scope, request));
        }

        await using AsyncServiceScope di = _v4Host.Host.Services.CreateAsyncScope();
        bool fired = false;
        Dhcpv4LeaseAllocationService service = BuildV4Service(
            di.ServiceProvider,
            _v4Host.Host.Services,
            scope.Name,
            "rename-race-v4-confirm-new",
            () => fired = true
        );

        // The REQUEST updates the tracked row, which still carries the old name.
        LeaseEntity lease = await service.ConfirmAsync(
            scope,
            request,
            IPAddress.Parse("192.0.2.30"),
            TimeSpan.FromHours(1)
        );

        Assert.True(fired);
        Assert.NotNull(lease);
        await AssertOnlyUnderAsync(
            _v4Host.Host.Services,
            "rename-race-v4-confirm-new",
            scope.Name,
            lease.ClientIdentifier
        );
    }

    [Fact]
    public async Task OfferV6_WhenTheScopeIsRenamedDuringTheRequest_StoresTheRowUnderTheNewName()
    {
        DhcpScopeEntity scope = await _v6Host.SeedAddressScopeAsync(
            "rename-race-v6-offer",
            "2001:db8:2::10",
            "2001:db8:2::20"
        );

        await using AsyncServiceScope di = _v6Host.Host.Services.CreateAsyncScope();
        bool fired = false;
        Dhcpv6LeaseAllocationService service = BuildV6Service(
            di.ServiceProvider,
            _v6Host.Host.Services,
            scope.Name,
            "rename-race-v6-offer-new",
            () => fired = true
        );

        LeaseEntity lease = await service.OfferAddressAsync(
            scope,
            "00:03:00:01:b1:00:00:00:00:01",
            new Dhcpv6IdentityAssociationForNonTemporaryAddresses { IdentityAssociationId = 1 }
        );

        Assert.True(fired);
        Assert.NotNull(lease);
        await AssertOnlyUnderAsync(
            _v6Host.Host.Services,
            "rename-race-v6-offer-new",
            scope.Name,
            lease.ClientIdentifier
        );
    }

    /// <summary>
    ///     This method builds the DHCPv4 service with a lease repository that runs the rename
    ///     race (see <see cref="WrapLeaseRepository" />); a null <paramref name="newName" />
    ///     builds it without a race.
    /// </summary>
    private static Dhcpv4LeaseAllocationService BuildV4Service(
        IServiceProvider provider,
        IServiceProvider hostServices,
        string oldName,
        string newName,
        Action onFired
    ) =>
        new(
            WrapLeaseRepository(provider, hostServices, oldName, newName, onFired),
            provider.GetRequiredService<IDhcpReservationRepository>(),
            provider.GetRequiredService<IDhcpScopeRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IDhcpServerIdentifierProvider>(),
            provider.GetRequiredService<IOptions<DhcpServerOptions>>(),
            new NullPublisher(),
            NullLogger<Dhcpv4LeaseAllocationService>.Instance
        );

    /// <summary>
    ///     This method builds the DHCPv6 service the same way.
    /// </summary>
    private static Dhcpv6LeaseAllocationService BuildV6Service(
        IServiceProvider provider,
        IServiceProvider hostServices,
        string oldName,
        string newName,
        Action onFired
    ) =>
        new(
            WrapLeaseRepository(provider, hostServices, oldName, newName, onFired),
            provider.GetRequiredService<IDhcpReservationRepository>(),
            provider.GetRequiredService<IDhcpScopeRepository>(),
            provider.GetRequiredService<IUnitOfWork>(),
            provider.GetRequiredService<IOptions<DhcpServerOptions>>(),
            new NullPublisher(),
            NullLogger<Dhcpv6LeaseAllocationService>.Instance
        );

    /// <summary>
    ///     This method wraps the real lease repository. The first
    ///     <c>ListByScopeNameAsync(oldName)</c> reads the rows under the old name, then commits
    ///     the rename in a separate scope and returns the stale rows, so the service goes on with
    ///     rows that carry the old name.
    /// </summary>
    private static ILeaseRepository WrapLeaseRepository(
        IServiceProvider provider,
        IServiceProvider hostServices,
        string oldName,
        string newName,
        Action onFired
    )
    {
        ILeaseRepository real = provider.GetRequiredService<ILeaseRepository>();
        if (newName is null)
            return real;

        ILeaseRepository proxy = DispatchProxy.Create<ILeaseRepository, DelegateProxy>();
        bool done = false;
        ((DelegateProxy)(object)proxy).Handler = (method, args) =>
        {
            object result;
            try
            {
                result = method.Invoke(real, args);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }

            if (
                done
                || method.Name != nameof(ILeaseRepository.ListByScopeNameAsync)
                || !string.Equals((string)args[0], oldName, StringComparison.Ordinal)
            )
                return result;

            done = true;
            return RenameAfterAsync(
                (Task<IReadOnlyList<LeaseEntity>>)result,
                hostServices,
                oldName,
                newName,
                onFired
            );
        };
        return proxy;
    }

    private static async Task<IReadOnlyList<LeaseEntity>> RenameAfterAsync(
        Task<IReadOnlyList<LeaseEntity>> read,
        IServiceProvider hostServices,
        string oldName,
        string newName,
        Action onFired
    )
    {
        IReadOnlyList<LeaseEntity> stale = await read;

        // The committed rename, as DhcpScopesController does it: scope and leases together.
        await using AsyncServiceScope scope = hostServices.CreateAsyncScope();
        IDhcpScopeRepository scopes =
            scope.ServiceProvider.GetRequiredService<IDhcpScopeRepository>();
        ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        DhcpScopeEntity entity = await scopes.FindByNameAsync(oldName);
        entity.Name = newName;
        scopes.Update(entity);
        foreach (LeaseEntity lease in await leases.ListByScopeNameAsync(oldName))
        {
            lease.ScopeName = newName;
            leases.Update(lease);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        onFired();
        return stale;
    }

    private static async Task AssertOnlyUnderAsync(
        IServiceProvider hostServices,
        string newName,
        string oldName,
        string clientIdentifier
    )
    {
        await using AsyncServiceScope scope = hostServices.CreateAsyncScope();
        ILeaseRepository leases = scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        Assert.NotNull(await leases.FindByClientIdentifierAsync(newName, clientIdentifier));
        Assert.Null(await leases.FindByClientIdentifierAsync(oldName, clientIdentifier));
        Assert.Empty(await leases.ListByScopeNameAsync(oldName));
    }

    private static Dhcpv4Message NewV4Message(byte[] clientHardwareAddress)
    {
        Dhcpv4Message message = new()
        {
            OperationCode = Dhcpv4OperationCodeEnum.BootRequest,
            TransactionId = 0x42,
            HardwareAddressLength = (byte)clientHardwareAddress.Length,
            ClientHardwareAddress = clientHardwareAddress,
        };
        message.SetMessageType(Dhcpv4MessageTypeEnum.Discover);
        return message;
    }

    /// <summary>This class is a dispatch proxy that hands every call to <see cref="Handler" />.</summary>
    public class DelegateProxy : DispatchProxy
    {
        /// <summary>This property is the function that answers each call.</summary>
        public Func<MethodInfo, object[], object> Handler { get; set; }

        /// <inheritdoc />
        protected override object Invoke(MethodInfo targetMethod, object[] args) =>
            Handler(targetMethod, args);
    }

    /// <summary>This class is a publisher that drops every event.</summary>
    private sealed class NullPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
