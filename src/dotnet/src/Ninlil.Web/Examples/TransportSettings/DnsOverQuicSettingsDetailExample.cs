using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsOverQuicSettingsDetail" />, as
///     returned by <see cref="Controllers.TransportSettingsController.GetDnsOverQuic" />.
/// </summary>
public class DnsOverQuicSettingsDetailExample : IExamplesProvider<DnsOverQuicSettingsDetail>
{
    /// <summary>
    ///     This method returns DNS-over-QUIC enabled with the default port, timeout, stream
    ///     and connection limits and a self-signed certificate (no PFX path).
    /// </summary>
    public DnsOverQuicSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 853,
            ListenAddresses: [],
            IdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentBidirectionalStreams: 100,
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
