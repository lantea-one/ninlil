using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>webhookDelivery</c> health check of <c>GET /status</c>: has
///     any webhook delivery used up its retries
///     (<see cref="WebhookDeliveryAttemptStatusEnum.DeadLettered" />) within
///     <see cref="RecentWindow" />? It uses the generic <c>ListAsync(predicate)</c> of
///     <see cref="IWebhookDeliveryAttemptRepository" />, which both storage providers translate
///     into a filtered query (a SQL <c>WHERE</c> or a MongoDB filter), so no rows are loaded and
///     filtered in memory.
///     <para>
///         <b>It does not check that <c>WebhookDeliveryRetrySweepService</c> is running.</b> A
///         stalled sweep leaves deliveries retrying without ever dead-lettering, so this check
///         stays healthy in that case. There is no cheap liveness signal for the sweep, and the
///         case an operator most needs to see is a subscriber endpoint that keeps failing.
///     </para>
///     <para>
///         <b>Degraded, never Unhealthy:</b> a dead-lettered delivery means a subscriber's
///         endpoint is down, misconfigured or rejecting the payload, a fault outside this server,
///         as with an unreachable forwarder in <see cref="UpstreamForwarderHealthCheck" />.
///     </para>
/// </summary>
/// <param name="scopeFactory">The factory used to reach a scoped <see cref="IWebhookDeliveryAttemptRepository" />.</param>
public sealed class WebhookDeliveryHealthCheck(IServiceScopeFactory scopeFactory) : IHealthCheck
{
    /// <summary>
    ///     This field contains how far back the check looks for a dead-lettered delivery: one hour,
    ///     so the warning clears an hour after the last dead-lettered delivery.
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
