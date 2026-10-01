// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the webhook delivery settings (<c>WebhookDeliveryOptions</c>) that
///     <c>GET /api/webhook-delivery-settings</c> returns: the retry backoff and HTTP timeout every
///     subscription's deliveries use. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="InitialRetryDelay">The wait before the first retry of a failed delivery, doubled for each later retry. Defaults to 1 second.</param>
/// <param name="MaxRetryDelay">The longest wait between retries. Defaults to 30 seconds.</param>
/// <param name="RequestTimeout">How long one delivery request may take. Defaults to 10 seconds; a change applies as pooled HTTP handlers are replaced.</param>
public record WebhookDeliverySettingsDetail(
    TimeSpan InitialRetryDelay,
    TimeSpan MaxRetryDelay,
    TimeSpan RequestTimeout
);
