using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.TransportSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsOverQuicSettingsDetail" />, referenced by
///     <see cref="Controllers.TransportSettingsController.GetDnsOverQuic" />.
/// </summary>
public class DnsOverQuicSettingsDetailExample : IExamplesProvider<DnsOverQuicSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: this transport
    ///     enabled, this class's own compiled-in default port/timeout/stream limit, and a
    ///     self-signed certificate (no operator-provided PFX configured).
    /// </summary>
    public DnsOverQuicSettingsDetail GetExamples() =>
        new(
            Enable: true,
            Port: 853,
            ListenAddresses: [],
            IdleTimeout: TimeSpan.FromSeconds(30),
            MaxConcurrentBidirectionalStreams: 100,
            Certificate: new TlsCertificateSettingsDetail(
                PfxPath: "",
                PfxPasswordConfigured: false,
                SelfSignedSubjectAlternativeNames: ["localhost"],
                CertificateReloadPollInterval: TimeSpan.FromMinutes(5),
                AllowedProtocols: []
            )
        );
}
