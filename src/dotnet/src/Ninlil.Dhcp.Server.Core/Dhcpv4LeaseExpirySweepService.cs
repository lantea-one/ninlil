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
///     This background service notices when DHCP leases expire. No client message announces an
///     expiry, unlike a RELEASE (see <see cref="Allocation.Dhcpv4LeaseAllocationService.ReleaseAsync" />),
///     so every <see cref="DhcpServerOptions.LeaseExpirySweepInterval" /> it lists the
///     <see cref="LeaseStateEnum.Active" /> leases, marks those whose <c>LeaseExpiresAtUtc</c> has
///     passed as <see cref="LeaseStateEnum.Expired" />, and publishes a
///     <see cref="DhcpLeaseChangedEvent" /> for each; the DHCP-to-DNS integration uses that to
///     remove the client's A/AAAA record.
///     <para>
///         Despite the name it handles DHCPv4 and DHCPv6 leases alike (it does not filter on
///         <see cref="LeaseEntity.ProtocolVersion" />). <c>Program.cs</c> registers it for every
///         configuration, whichever engine is active, including a relay agent that grants no
///         leases itself, so recorded leases still age out.
///     </para>
///     <para>
///         Like <c>Ninlil.Dns.Server.Webhooks.WebhookDeliveryRetrySweepService</c>, the first
///         sweep runs at startup and a failed sweep is logged without stopping the service. The
///         delay sits inside the same <c>try</c>, so after a failure the next sweep starts at once
///         rather than one interval later.
///     </para>
///     <para>
///         The query filters by state only and compares <c>LeaseExpiresAtUtc</c> in memory, as
///         <see cref="Allocation.Dhcpv4LeaseAllocationService.IsLeaseBlocking" /> does: the SQLite
///         provider does not translate every <c>DateTimeOffset</c> comparison (it cannot order by
///         one at all), and lease counts are small.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory that creates a scope for each sweep.</param>
/// <param name="options">The server options, read for the sweep interval on every pass.</param>
/// <param name="logger">The logger each sweep's outcome is recorded to.</param>
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
                // Expected on shutdown.
            }
            catch (Exception exception)
            {
                // A failed sweep must not stop the service; the next pass picks the leases up.
                logger.LogWarning(exception, "A DHCP lease expiry sweep failed unexpectedly.");
            }
        }
    }

    /// <summary>
    ///     This method runs one sweep in a new service scope. It is public so tests can run a pass
    ///     without waiting for <see cref="DhcpServerOptions.LeaseExpirySweepInterval" />. Offered
    ///     leases and declined-address quarantines are not touched; their expiry time alone makes
    ///     them free.
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
