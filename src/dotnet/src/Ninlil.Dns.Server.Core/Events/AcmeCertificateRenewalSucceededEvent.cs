// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <c>AcmeCertificateRenewalService</c> (Server.Acme) issues or
///     renews a domain's RFC 8555 certificate. It is separate from
///     <see cref="AcmeCertificateRenewalFailedEvent" /> so a webhook can alert on failures
///     without being told about every routine renewal.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the certificate was issued or renewed.</param>
/// <param name="Domain">The domain the certificate was issued or renewed for.</param>
/// <param name="ExpiresAtUtc">The new certificate's expiry date and time.</param>
public record AcmeCertificateRenewalSucceededEvent(
    DateTimeOffset OccurredAtUtc,
    string Domain,
    DateTimeOffset ExpiresAtUtc
) : IDnsServerEvent;
