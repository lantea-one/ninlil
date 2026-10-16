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
    ///     port (5443), limits and certificate timing. The provider name carries the
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
            CertificateValidityDuration: TimeSpan.FromHours(48),
            CertificateClockSkewTolerance: TimeSpan.FromMinutes(5)
        );
}
