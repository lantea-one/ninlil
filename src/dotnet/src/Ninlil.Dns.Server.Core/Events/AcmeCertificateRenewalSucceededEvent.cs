// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever <c>Server.Acme</c>'s own <c>AcmeCertificateRenewalService</c>
///     successfully issues or renews a domain's own RFC 8555 certificate. Deliberately kept as
///     its own event type separate from
///     <see cref="AcmeCertificateRenewalFailedEvent" /> -- a webhook subscriber, exactly like a
///     SendGrid Event Webhook consumer, very plausibly wants only failures (an operational alert)
///     without also being notified on every single routine, expected 60-to-90-day renewal
///     success.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the certificate was issued or renewed.</param>
/// <param name="Domain">The domain the certificate was issued or renewed for.</param>
/// <param name="ExpiresAtUtc">The newly issued certificate's own expiration date and time.</param>
public record AcmeCertificateRenewalSucceededEvent(
    DateTimeOffset OccurredAtUtc,
    string Domain,
    DateTimeOffset ExpiresAtUtc
) : IDnsServerEvent;
