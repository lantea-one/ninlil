using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "webhook delivery health" check, an addition to
///     <c>GET /status</c>'s existing checklist. It answers a narrow, deliberately cheap question:
///     has any outbound webhook delivery exhausted its own retry budget
///     (<see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />) recently, within
///     <see cref="RecentWindow" />. This reuses
///     <see cref="IWebhookDeliveryAttemptRepository" />'s own already-established generic
///     <c>ListAsync(predicate)</c> lookup -- a real, filtered query against the underlying storage
///     engine on both supported providers (a translated SQL <c>WHERE</c> on a relational engine,
///     a translated MongoDB filter document otherwise), never a full-table scan materialized into
///     memory first -- rather than adding a new, dedicated repository method for a query shape
///     the existing generic lookup already covers efficiently.
///     <para>
///         <b>Why "recently dead-lettered" rather than "is the sweep service itself running"</b>:
///         a stalled or crashed <c>WebhookDeliveryRetrySweepService</c> is already indirectly
///         visible through this same signal (a delivery stuck retrying forever never reaches
///         <c>DeadLettered</c> at all if the sweep has stopped resuming it, so its own absence
///         from this check's own dead-letter window is not itself proof of health) -- but there is
///         no cheap, independent "is this specific <see cref="Microsoft.Extensions.Hosting.BackgroundService" />
///         still alive" signal available to a health check running in a different DI scope, and a
///         dedicated one would need new, dedicated bookkeeping (a last-swept-at timestamp) this
///         sub-phase's own scope note explicitly cautions against inventing new machinery where an
///         existing, cheap query already covers the operationally meaningful case: an operator
///         needs to know a real subscriber endpoint is failing, not that the sweep loop's own
///         internal clock ticked recently.
///     </para>
///     <para>
///         <b>Degraded, never Unhealthy</b>: an exhausted delivery means one subscriber's own
///         endpoint is unreachable, misconfigured, or persistently rejecting the payload -- an
///         external dependency's own failure, never a fault in this server's own DNS-answering or
///         any other subsystem, the same "a symptom of an external dependency, not of this server
///         itself" reasoning <see cref="UpstreamForwarderHealthCheck" /> already applies to an
///         unreachable upstream forwarder.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IWebhookDeliveryAttemptRepository" />.</param>
public sealed class WebhookDeliveryHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    /// <summary>
    ///     This constant contains how far back this check looks for a dead-lettered delivery --
    ///     long enough that a single sweep-interval hiccup never falsely flags this check, short
    ///     enough that a genuinely ongoing subscriber outage is still caught promptly rather than
    ///     staying invisible for a long stretch after the fact.
    /// </summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromHours(1);

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IWebhookDeliveryAttemptRepository attemptRepository =
            scope.ServiceProvider.GetRequiredService<IWebhookDeliveryAttemptRepository>();

        DateTimeOffset cutoffUtc = DateTimeOffset.UtcNow - RecentWindow;

        IReadOnlyList<WebhookDeliveryAttemptEntity> recentlyDeadLettered =
            await attemptRepository.ListAsync(
                attempt =>
                    attempt.Status == WebhookDeliveryAttemptStatusEnum.DeadLettered
                    && attempt.CompletedAtUtc != null
                    && attempt.CompletedAtUtc >= cutoffUtc,
                cancellationToken
            );

        if (recentlyDeadLettered.Count == 0)
            return HealthCheckResult.Healthy(
                "No webhook deliveries have exhausted their retries in the last hour."
            );

        List<string> affectedSubscriptionNames =
        [
            .. recentlyDeadLettered
                .Select(attempt => attempt.SubscriptionName)
                .Distinct(StringComparer.Ordinal),
        ];

        return HealthCheckResult.Degraded(
            $"{recentlyDeadLettered.Count} webhook "
                + (recentlyDeadLettered.Count == 1 ? "delivery has" : "deliveries have")
                + " exhausted their retries in the last hour, affecting subscription(s): "
                + $"{string.Join(", ", affectedSubscriptionNames)}."
        );
    }
}
