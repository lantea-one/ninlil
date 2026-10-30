using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsCryptSettingsDetail" />, as
///     returned by <see cref="Controllers.TransportSettingsController.GetDnsCrypt" />.
/// </summary>
public class DnsCryptSettingsDetailExample : IExamplesProvider<DnsCryptSettingsDetail>
{
    /// <summary>
    ///     This method returns DNSCrypt enabled with a provider name, both ciphers and the default
    ///     port (5443), limits and certificate timing. A certificate is valid for 28 hours (shown
    ///     as <c>1.04:00:00</c>): the 24-hour rotation interval plus the 4 hours the previous
    ///     certificate stays valid (DNSCrypt v2 section 13). The provider name carries the
    ///     <c>2.dnscrypt-cert.</c> prefix, the form a client's <c>sdns://</c> stamp holds; the
    ///     bare <c>resolver.example</c> publishes at the same name.
    /// </summary>
    public DnsCryptSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 5443,
            ListenAddresses: [],
            ProviderName: "2.dnscrypt-cert.resolver.example.",
            TcpIdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentTcpConnections: 512,
            EnabledEsVersions: ["XChaCha20Poly1305", "XSalsa20Poly1305"],
            CertificateRotationInterval: TimeSpan.FromHours(24),
            CertificateValidityDuration: TimeSpan.FromHours(28),
            CertificateClockSkewTolerance: TimeSpan.FromMinutes(5)
        );
}
