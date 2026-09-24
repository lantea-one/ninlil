using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.ReverseProxy;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ReverseProxySettingsDetail" />, referenced by
///     <see cref="Controllers.ReverseProxySettingsController.Get" />.
/// </summary>
public class ReverseProxySettingsDetailExample : IExamplesProvider<ReverseProxySettingsDetail>
{
    /// <summary>
    ///     This method returns a single trusted reverse proxy on the same private network as this
    ///     server, with a shared secret configured (never revealed) and DNS-over-TLS's own
    ///     entirely separate PROXY protocol trust left disabled -- a realistic "only the HTTP
    ///     side sits behind a reverse proxy" deployment shape.
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
