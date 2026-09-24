using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsOverTlsSettingsDetail" />, referenced by
///     <see cref="Controllers.TransportSettingsController.GetDnsOverTls" />.
/// </summary>
public class DnsOverTlsSettingsDetailExample : IExamplesProvider<DnsOverTlsSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: this transport
    ///     enabled, this class's own compiled-in default port/timeouts/connection limit, and a
    ///     self-signed certificate (no operator-provided PFX configured).
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
