using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Forwarding;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "forwarder health" check, reusing --rather than
///     re-checking independently -- <see cref="ForwarderHealthTracker" />'s own already-tracked
///     per-forwarder health for every currently enabled <see cref="DnsForwarderEntity" />. When
///     <see cref="ForwardingOptions.Enable" /> is <see langword="false" />, or no forwarder is
///     configured at all, this reports <see cref="HealthStatus.Healthy" /> trivially -- forwarding
///     is a capability-expanding toggle (invariant #6), not something whose absence is itself a
///     problem.
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IDnsForwarderRepository" />.</param>
/// <param name="forwarderHealthTracker">
///     The tracker this check reads every configured forwarder's own already-tracked health
///     from -- registered unconditionally regardless of <see cref="ForwardingOptions.Enable" />,
///     the same "always registered, gated internally" shape every other health-checked
///     subsystem here follows.
/// </param>
/// <param name="forwardingOptions">
///     This host's own forwarding configuration, read live via
///     <see cref="IOptionsMonitor{TOptions}" /> -- <see cref="ForwardingOptions.Enable" /> is now
///     always live (see <c>ServiceCollectionForwardingExtension</c>'s own doc comment), so this
///     check's own "disabled" report must reflect the same, currently effective value, not
///     whatever it happened to be at process startup.
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

        // A degraded, not unhealthy, overall result -- ForwarderQueryResolver already fails over
        // to the next healthy forwarder, and ultimately to this server's own recursive
        // resolution, so one bad forwarder does not mean this server cannot resolve names at all.
        return HealthCheckResult.Degraded(
            $"{unhealthyForwarders.Count} of {enabledForwarders.Count} configured forwarder(s) are currently unhealthy: {string.Join(", ", unhealthyForwarders)}."
        );
    }
}
