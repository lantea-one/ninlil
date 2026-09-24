using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dhcp.Data.Abstractions.Repository;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core;

/// <summary>
///     This class is the one mechanism that ever detects a DHCP lease's own natural expiry --
///     neither DHCPv4 nor DHCPv6 has a wire message a client sends to announce it, unlike a
///     RELEASE, so this project cannot simply react to an incoming request the way
///     <see cref="Allocation.Dhcpv4LeaseAllocationService.ReleaseAsync" /> does. Despite its own
///     DHCPv4-era name, this class is, and always has been, fully protocol-agnostic -- it queries
///     <see cref="LeaseEntity" /> generically, with no filter on
///     <see cref="LeaseEntity.ProtocolVersion" /> at all -- and now genuinely does sweep both
///     DHCPv4 and DHCPv6 leases; it is registered directly by the composition root
///     (<c>Program.cs</c>), independently of, and unconditionally alongside, whichever specific
///     DHCPv4/DHCPv6 engine variant is actually active (including a DHCPv4 relay agent, which
///     grants no leases of its own but still benefits from existing leases eventually aging out).
///     On a fixed interval (<see cref="DhcpServerOptions.LeaseExpirySweepInterval" />), it lists
///     every currently-<see cref="LeaseStateEnum.Active" /> lease, marks any whose own
///     <c>LeaseExpiresAtUtc</c> has already passed as <see cref="LeaseStateEnum.Expired" />, and
///     publishes <see cref="DhcpLeaseChangedEvent" /> for each one -- most notably consumed by the
///     DHCP-&gt;DNS integration feature to remove that client's own now-stale A/AAAA record.
///     <para>
///         Mirrors <c>Ninlil.Dns.Server.Webhooks.WebhookDeliveryRetrySweepService</c>'s own shape
///         exactly: the first sweep runs immediately rather than after waiting out one full
///         interval, and a single failed pass (a transient storage error) is logged and never
///         takes the whole loop down -- there is always another sweep, at worst one interval
///         later.
///     </para>
///     <para>
///         Deliberately filters to <see cref="LeaseStateEnum.Active" /> leases only, then checks
///         <c>LeaseExpiresAtUtc</c> against the current time in memory rather than pushing that
///         comparison down into the repository's own query -- the same reasoning
///         <see cref="Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking" /> already applies:
///         this project's own SQLite provider does not reliably translate every comparison over a
///         raw <c>DateTimeOffset</c> column (it cannot order by one at all), and this
///         project's real-world lease counts are small enough that an equality filter plus an
///         in-memory scan costs nothing worth avoiding.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach fresh, scoped services per sweep.</param>
/// <param name="options">This server's own configured sweep interval.</param>
/// <param name="logger">The logger this service records the outcome of each sweep to.</param>
public sealed class Dhcpv4LeaseExpirySweepService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<DhcpServerOptions> options,
    ILogger<Dhcpv4LeaseExpirySweepService> logger
) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
                await Task.Delay(options.CurrentValue.LeaseExpirySweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during a graceful shutdown.
            }
            catch (Exception exception)
            {
                // One bad sweep must never take this whole background service down -- the same
                // reasoning WebhookDeliveryRetrySweepService's own sweep loop already establishes.
                // A lease genuinely due to expire simply gets noticed one interval later.
                logger.LogWarning(exception, "A DHCP lease expiry sweep failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method performs a single sweep pass against fresh, scoped services -- exposed
    ///     publicly so a test can drive and observe one pass directly rather than waiting out a
    ///     real <see cref="DhcpServerOptions.LeaseExpirySweepInterval" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>How many leases this pass expired.</returns>
    public async Task<int> SweepAsync(CancellationToken stoppingToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();

        ILeaseRepository leaseRepository =
            scope.ServiceProvider.GetRequiredService<ILeaseRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDnsServerEventPublisher eventPublisher =
            scope.ServiceProvider.GetRequiredService<IDnsServerEventPublisher>();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<LeaseEntity> activeLeases = await leaseRepository.ListAsync(
            lease => lease.State == LeaseStateEnum.Active,
            stoppingToken
        );

        List<LeaseEntity> expired =
        [
            .. activeLeases.Where(lease =>
                lease.LeaseExpiresAtUtc.HasValue && lease.LeaseExpiresAtUtc.Value <= now
            ),
        ];

        if (expired.Count == 0)
            return 0;

        foreach (LeaseEntity lease in expired)
        {
            lease.State = LeaseStateEnum.Expired;
            lease.UpdatedAtUtc = now;
            leaseRepository.Update(lease);
        }

        await unitOfWork.SaveChangesAsync(stoppingToken);

        foreach (LeaseEntity lease in expired)
            eventPublisher.Publish(
                new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Expired)
            );

        logger.LogInformation("DHCP lease expiry sweep expired {Count} lease(s).", expired.Count);

        return expired.Count;
    }
}
