// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever <c>Server.Acme</c>'s own <c>AcmeCertificateRenewalService</c>
///     fails to issue or renew a domain's own RFC 8555 certificate (a CA outage, a DNS-01
///     challenge that failed to validate, a domain no longer locally hosted). Kept as its own
///     event type separate from <see cref="AcmeCertificateRenewalSucceededEvent" /> -- see that
///     type's own remarks for why.
///     A single failure here does not necessarily mean the domain's own certificate has actually
///     expired yet; <c>AcmeCertificateRenewalService</c> retries again at its own next poll
///     interval regardless.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the failure occurred.</param>
/// <param name="Domain">The domain the issuance or renewal attempt was for.</param>
/// <param name="ErrorMessage">A short, human-readable description of why the attempt failed.</param>
public record AcmeCertificateRenewalFailedEvent(
    DateTimeOffset OccurredAtUtc,
    string Domain,
    string ErrorMessage
) : IDnsServerEvent;
