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
    ///     port, limits and certificate timing. The provider name already starts with
    ///     <c>2.dnscrypt-cert.</c>, which <c>DnsCryptCertificateDiscoveryResponder</c> adds again,
    ///     so with this value the certificates would be published under a doubled prefix.
    /// </summary>
    public DnsCryptSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 5353,
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
