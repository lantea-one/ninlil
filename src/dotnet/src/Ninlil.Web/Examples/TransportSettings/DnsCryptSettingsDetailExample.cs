using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsCryptSettingsDetail" />, referenced by
///     <see cref="Controllers.TransportSettingsController.GetDnsCrypt" />.
/// </summary>
public class DnsCryptSettingsDetailExample : IExamplesProvider<DnsCryptSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: this transport
    ///     enabled, a real provider zone an operator controls, both supported ciphers enabled, and
    ///     this class's own compiled-in default certificate rotation policy.
    /// </summary>
    public DnsCryptSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 5353,
            ProviderName: "2.dnscrypt-cert.resolver.example.",
            TcpIdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentTcpConnections: 512,
            EnabledEsVersions: ["XChaCha20Poly1305", "XSalsa20Poly1305"],
            CertificateRotationInterval: TimeSpan.FromHours(24),
            CertificateValidityDuration: TimeSpan.FromHours(48),
            CertificateClockSkewTolerance: TimeSpan.FromMinutes(5)
        );
}
