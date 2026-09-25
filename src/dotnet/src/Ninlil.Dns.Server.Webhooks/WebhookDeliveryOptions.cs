// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class binds to the <c>DnsServer:Webhooks:Delivery</c> section of the application's
///     configuration and holds the HTTP timeout and retry backoff of
///     <see cref="WebhookDeliveryService" /> and the sweep interval and retention period of
///     <see cref="WebhookDeliveryRetrySweepService" />. The maximum number of attempts is set per
///     subscription (<c>WebhookSubscriptionEntity.MaxDeliveryAttempts</c>); these settings are
///     the timing all subscriptions share. The backoff starts at an initial delay and doubles up
///     to a ceiling, and a delivery that runs out of attempts is dead-lettered and logged. All
///     values are read at use, so changes apply without a restart.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Webhooks:Delivery";

    /// <summary>
    ///     This property contains the delay before the first retry after a first attempt fails (a
    ///     non-2xx response or a transport failure). The retry is made by the first sweep after
    ///     the delay, so it can come up to <see cref="SweepInterval" /> later.
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     This property contains the longest delay between two attempts of the same delivery.
    ///     The delay doubles after each failed attempt from <see cref="InitialRetryDelay" /> up to
    ///     this ceiling, so a large <c>MaxDeliveryAttempts</c> does not produce ever-growing
    ///     waits.
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains the timeout of each delivery request, applied to the
    ///     <see cref="HttpClient" /> created for every attempt.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    ///     This property contains how often <see cref="WebhookDeliveryRetrySweepService" /> looks
    ///     for saved deliveries due for a retry. All retries are made by that sweep, so a
    ///     delivery waiting out its backoff survives a restart. A shorter interval keeps retries
    ///     closer to their computed time at the cost of more frequent (cheap) queries. It is also
    ///     how long a delivery to a disabled subscription is deferred.
    /// </summary>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This property contains how long a delivery that has already reached a terminal outcome
    ///     (<see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Succeeded" /> or
    ///     <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.DeadLettered" />)
    ///     is kept, counted from its completion, before <see cref="WebhookDeliveryRetrySweepService" />
    ///     removes it, so the table does not grow without bound on a long-running server (as
    ///     <c>QueryLoggingOptions.RetentionPeriod</c> does for the query log). Zero or less turns
    ///     removal off. A <see cref="Ninlil.Dns.Common.Enum.WebhookDeliveryAttemptStatusEnum.Pending" />
    ///     delivery is never removed, whatever its age, since it may still be retried.
    /// </summary>
    public TimeSpan AttemptRetentionPeriod { get; set; } = TimeSpan.FromDays(30);
}
