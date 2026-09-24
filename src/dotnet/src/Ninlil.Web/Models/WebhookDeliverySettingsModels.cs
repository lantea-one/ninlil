// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>WebhookDeliveryOptions</c> -- the shared retry/backoff timing and outbound HTTP timeout
///     every webhook subscription's own deliveries use. This record's own fields are the exact
///     same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record WebhookDeliverySettingsDetail(
    TimeSpan InitialRetryDelay,
    TimeSpan MaxRetryDelay,
    TimeSpan RequestTimeout
);
