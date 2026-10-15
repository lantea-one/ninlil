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
///         delay follows the sweep in every case, so a persistent error costs one warning per
///         interval and never a busy loop. An interval of zero or less would spin or throw, so
///         it is replaced by the default of one minute, with one warning per service start.
///     </para>
///     <para>
///         The query filters by state and expiry in the database: the lease instants are stored as
///         UTC ticks, so the comparison translates on every engine. Only the leases that have
///         expired are loaded. Each one is then expired with
///         <see cref="ILeaseRepository.TryExpireAsync" />, a single conditional write, so a
///         renewal that commits between the load and the write is never overwritten.
///     </para>
///     <para>
///         Accepted limit: if the sweep commits first and a renewal then writes the row back as
///         active, the client keeps its lease, but an <see cref="DhcpLeaseChangeKindEnum.Expired" />
///         and a <see cref="DhcpLeaseChangeKindEnum.Granted" /> event are both published. The
///         subscribers run fire-and-forget, so the DNS record could end up removed.
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
    /// <summary>
    ///     This field is the interval used when the configured one is zero or negative.
    /// </summary>
    private static readonly TimeSpan DefaultSweepInterval = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool warnedAboutInterval = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Expected on shutdown.
                break;
            }
            catch (Exception exception)
            {
                // A failed sweep must not stop the service; the next pass picks the leases up.
                logger.LogWarning(exception, "A DHCP lease expiry sweep failed unexpectedly.");
            }

            TimeSpan interval = options.CurrentValue.LeaseExpirySweepInterval;

            if (interval <= TimeSpan.Zero)
            {
                // Task.Delay would spin on zero and throw on a negative value.
                if (!warnedAboutInterval)
                    logger.LogWarning(
                        "The DHCP lease expiry sweep interval {Interval} is not positive; using {Default} instead.",
                        interval,
                        DefaultSweepInterval
                    );

                warnedAboutInterval = true;
                interval = DefaultSweepInterval;
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
                break;
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
        IDnsServerEventPublisher eventPublisher =
            scope.ServiceProvider.GetRequiredService<IDnsServerEventPublisher>();

        DateTimeOffset now = DateTimeOffset.UtcNow;

        IReadOnlyList<LeaseEntity> candidates = await leaseRepository.ListAsync(
            lease =>
                lease.State == LeaseStateEnum.Active
                && lease.LeaseExpiresAtUtc != null
                && lease.LeaseExpiresAtUtc <= now,
            stoppingToken
        );

        int expired = 0;

        foreach (LeaseEntity lease in candidates)
        {
            // The conditional write loses to a renewal that committed first; then nothing is
            // expired and nothing is published for that lease.
            if (!await leaseRepository.TryExpireAsync(lease.Id, now, stoppingToken))
                continue;

            expired++;
            eventPublisher.Publish(
                new DhcpLeaseChangedEvent(now, lease.Id, DhcpLeaseChangeKindEnum.Expired)
            );
        }

        if (expired > 0)
            logger.LogInformation("DHCP lease expiry sweep expired {Count} lease(s).", expired);

        return expired;
    }
}
