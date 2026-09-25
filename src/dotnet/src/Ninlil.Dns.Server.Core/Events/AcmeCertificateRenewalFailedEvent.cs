// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when <c>AcmeCertificateRenewalService</c> (Server.Acme) fails to
///     issue or renew a domain's RFC 8555 certificate, for example because of a CA outage, a
///     failed DNS-01 challenge or a domain no longer hosted here. It is separate from
///     <see cref="AcmeCertificateRenewalSucceededEvent" /> so a webhook can subscribe to
///     failures only. One failure does not mean the certificate has expired; the service
///     retries at its next poll.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the failure occurred.</param>
/// <param name="Domain">The domain the issuance or renewal attempt was for.</param>
/// <param name="ErrorMessage">A short description of why the attempt failed.</param>
public record AcmeCertificateRenewalFailedEvent(
    DateTimeOffset OccurredAtUtc,
    string Domain,
    string ErrorMessage
) : IDnsServerEvent;
