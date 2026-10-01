using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Forwarding;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>forwarders</c> health check of <c>GET /status</c>. It reads
///     the health <see cref="ForwarderHealthTracker" /> already tracks for each enabled global
///     <see cref="DnsForwarderEntity" /> rather than probing them again. With
///     <see cref="ForwardingOptions.Enable" /> off, or no forwarder configured, it reports
///     <see cref="HealthStatus.Healthy" />: forwarding is optional, so its absence is not a
///     problem.
/// </summary>
/// <param name="scopeFactory">Opens a scope to reach the scoped <see cref="IDnsForwarderRepository" />.</param>
/// <param name="forwarderHealthTracker">
///     Supplies each forwarder's health. It is registered whether or not forwarding is enabled.
/// </param>
/// <param name="forwardingOptions">
///     The forwarding settings, read through <see cref="IOptionsMonitor{TOptions}" /> because
///     <see cref="ForwardingOptions.Enable" /> takes effect at once (see
///     <c>ServiceCollectionForwardingExtension</c>), so the check follows the current value.
/// </param>
public sealed class UpstreamForwarderHealthCheck(
    IServiceScopeFactory scopeFactory,
    ForwarderHealthTracker forwarderHealthTracker,
    IOptionsMonitor<ForwardingOptions> forwardingOptions
) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        if (!forwardingOptions.CurrentValue.Enable)
            return HealthCheckResult.Healthy("Upstream forwarding is disabled.");

        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsForwarderRepository forwarderRepository =
            scope.ServiceProvider.GetRequiredService<IDnsForwarderRepository>();

        IReadOnlyList<DnsForwarderEntity> enabledForwarders =
            await forwarderRepository.ListEnabledOrderedAsync(cancellationToken);

        if (enabledForwarders.Count == 0)
            return HealthCheckResult.Healthy(
                "Upstream forwarding is enabled, but no forwarder is currently configured."
            );

        List<string> unhealthyForwarders =
        [
            .. enabledForwarders
                .Where(forwarder => !forwarderHealthTracker.IsHealthy(forwarder.DefinitionString))
                .Select(forwarder => forwarder.Name),
        ];

        if (unhealthyForwarders.Count == 0)
            return HealthCheckResult.Healthy(
                $"All {enabledForwarders.Count} configured forwarder(s) are healthy."
            );

        // Degraded, not Unhealthy: ForwarderQueryResolver moves on to the next forwarder, and the
        // dispatcher falls back to recursion when all fail, so names still resolve.
        return HealthCheckResult.Degraded(
            $"{unhealthyForwarders.Count} of {enabledForwarders.Count} configured forwarder(s) are currently unhealthy: {string.Join(", ", unhealthyForwarders)}."
        );
    }
}
