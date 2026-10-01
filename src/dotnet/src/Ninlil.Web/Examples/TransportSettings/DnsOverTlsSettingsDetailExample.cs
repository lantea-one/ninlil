using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsOverTlsSettingsDetail" />, as
///     returned by <see cref="Controllers.TransportSettingsController.GetDnsOverTls" />.
/// </summary>
public class DnsOverTlsSettingsDetailExample : IExamplesProvider<DnsOverTlsSettingsDetail>
{
    /// <summary>
    ///     This method returns DNS-over-TLS enabled with the default port, timeout and connection
    ///     limit and a self-signed certificate (no PFX path).
    /// </summary>
    public DnsOverTlsSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 853,
            ListenAddresses: [],
            IdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentConnections: 512,
            Certificate: new TlsCertificateSettingsDetail(
                PfxPath: "",
                PfxPasswordConfigured: false,
                SelfSignedSubjectAlternativeNames: ["localhost"],
                CertificateReloadPollInterval: TimeSpan.FromMinutes(5),
                AllowedProtocols: []
            )
        );
}
