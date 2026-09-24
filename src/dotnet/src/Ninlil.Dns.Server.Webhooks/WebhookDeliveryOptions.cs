// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class binds to the <c>DnsServer:Webhooks:Delivery</c> section of the application's
///     configuration and controls <see cref="WebhookDeliveryService" />'s own outbound HTTP
///     timeout and retry/backoff timing, plus <see cref="WebhookDeliveryRetrySweepService" />'s
///     own resume/retention sweep cadence. A single subscription's own maximum attempt
///     <i>count</i> is per-subscription configuration
///     (<c>WebhookSubscriptionEntity.MaxDeliveryAttempts</c>); this class only controls the shared
///     timing every subscription's own retries use, mirroring
///     <c>RabbitMqEventBrokerOptions</c>'s own "initial delay, doubling up to a ceiling" backoff
///     shape -- the same "bounded retries with exponential backoff, then give up and log" middle
///     ground the message-broker transports' own outage handling already establishes.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Webhooks:Delivery";

    /// <summary>
    ///     This property contains how long <see cref="WebhookDeliveryService" /> waits before a
    ///     subscription's own first retry, after its first delivery attempt fails (a non-2xx
    ///     response or a transport-level failure).
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest <see cref="WebhookDeliveryService" /> ever waits
    ///     between two retries for the same delivery -- the retry delay doubles after each failed
    ///     attempt, starting from <see cref="InitialRetryDelay" />, up to this ceiling, so a
    ///     subscription configured with a large <c>MaxDeliveryAttempts</c> does not result in an
    ///     unreasonably long tail of ever-growing waits.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains the per-request timeout applied to every outbound delivery
    ///     attempt's own <see cref="HttpClient" />.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how often <see cref="WebhookDeliveryRetrySweepService" /> looks
    ///     for a persisted delivery that is now due for a retry attempt -- every second or later
    ///     attempt for any delivery is made exclusively from this sweep, never from an in-process
    ///     <see cref="Task.Delay(TimeSpan)" /> loop, precisely so a delivery still waiting out its
    ///     own backoff survives a process restart intact (see that class's own doc comment for the
    ///     full reasoning). A shorter interval delivers a retry closer to its own computed backoff
    ///     delay at the cost of more frequent, cheap polling queries; a longer interval trades the
    ///     opposite way.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This property contains how long a delivery that has already reached a terminal outcome
    ///     (<see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.DeadLettered" />)
    ///     is kept on record before <see cref="WebhookDeliveryRetrySweepService" />'s own
    ///     retention sweep removes it -- the same "a persisted operational record must eventually
    ///     age out, or it grows unboundedly on a server left running for years" reasoning
    ///     <c>QueryLoggingOptions.RetentionPeriod</c> already establishes for the query log. A
    ///     delivery still <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" />
    ///     is never removed by this sweep regardless of age, since it may still genuinely be
    ///     retried.
    /// </summary>
    public TimeSpan AttemptRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
}
