using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ReverseProxy;

/// <summary>
///     This class supplies the Swagger example for <see cref="ReverseProxySettingsDetail" />, as
///     returned by <see cref="Controllers.ReverseProxySettingsController.Get" />.
/// </summary>
public class ReverseProxySettingsDetailExample : IExamplesProvider<ReverseProxySettingsDetail>
{
    /// <summary>
    ///     This method returns one trusted reverse proxy on the server's private network with a
    ///     shared secret set (shown only as configured), while the DNS-over-TLS PROXY protocol
    ///     trust is off: only the HTTP side is behind a proxy.
    /// </summary>
    public ReverseProxySettingsDetail GetExamples() =>
        new(
            true,
            ["10.0.0.5"],
            ["10.0.0.0/24"],
            "X-Ninlil-Proxy-Secret",
            true,
            new DnsOverTlsProxyProtocolSettingsDetail(false, [], [])
        );
}
