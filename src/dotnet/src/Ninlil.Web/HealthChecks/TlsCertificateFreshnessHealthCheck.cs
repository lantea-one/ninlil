using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>tlsCertificate</c> health check of <c>GET /status</c>: how
///     close the HTTPS certificate is to expiry. It reads the Kestrel
///     <see cref="ReloadableTlsCertificateProvider" /> that <c>Program.cs</c> registers rather
///     than loading the file itself; that provider already polls for a certificate replaced on
///     disk by an operator or by <c>Ninlil.Dns.Server.Acme</c>. DNS-over-TLS and DNS-over-QUIC
///     present the same certificate when they share its <c>PfxPath</c> (by default, the same
///     self-signed one); a separately configured certificate for them is not checked.
/// </summary>
/// <param name="certificateProvider">The Kestrel certificate provider.</param>
public sealed class TlsCertificateFreshnessHealthCheck(
    ReloadableTlsCertificateProvider certificateProvider
) : IHealthCheck
{
    /// <summary>
    ///     This field contains how close to expiry the certificate must be for the check to report
    ///     <see cref="HealthStatus.Degraded" />: a week's warning before handshakes start failing.
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
