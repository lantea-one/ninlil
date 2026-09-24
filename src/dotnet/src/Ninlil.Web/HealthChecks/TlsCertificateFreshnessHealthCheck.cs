using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "TLS certificate freshness" health check, reusing --
///     rather than re-checking independently -- this host's own shared Kestrel
///     <see cref="ReloadableTlsCertificateProvider" /> (the same certificate DoT/DoQ also present,
///     per this project's own shared-certificate design), since a certificate an operator or
///     <c>Ninlil.Dns.Server.Acme</c> replaces on disk is picked up by that provider's own
///     background poll independently of this check.
/// </summary>
/// <param name="certificateProvider">This host's own shared Kestrel/DoT/DoQ certificate provider.</param>
public sealed class TlsCertificateFreshnessHealthCheck(
    ReloadableTlsCertificateProvider certificateProvider
) : IHealthCheck
{
    /// <summary>
    ///     This constant contains how close to expiry a certificate must be before this check
    ///     reports <see cref="HealthStatus.Degraded" /> rather than <see cref="HealthStatus.Healthy" />
    ///     -- a full week's own warning window before an expired certificate would actually start
    ///     failing TLS handshakes.
    /// </summary>
    private static readonly TimeSpan DegradedWarningWindow = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        X509Certificate2 certificate = certificateProvider.Current;

        if (certificate is null)
            return Task.FromResult(
                HealthCheckResult.Unhealthy("No TLS certificate is currently loaded.")
            );

        DateTime notAfterUtc = certificate.NotAfter.ToUniversalTime();
        DateTime nowUtc = DateTime.UtcNow;

        if (notAfterUtc <= nowUtc)
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"The current TLS certificate ('{certificate.Subject}') expired on {notAfterUtc:O}."
                )
            );

        if (notAfterUtc - nowUtc <= DegradedWarningWindow)
            return Task.FromResult(
                HealthCheckResult.Degraded(
                    $"The current TLS certificate ('{certificate.Subject}') expires soon, on {notAfterUtc:O}."
                )
            );

        return Task.FromResult(
            HealthCheckResult.Healthy(
                $"The current TLS certificate ('{certificate.Subject}') is valid until {notAfterUtc:O}."
            )
        );
    }
}
