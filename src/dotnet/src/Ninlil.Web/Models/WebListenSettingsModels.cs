// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the web listener settings (<c>WebListenOptions</c>) that
///     <c>GET /api/web-listen-settings</c> returns: the ports and certificate shared by the REST
///     API, the SignalR hub and DNS-over-HTTPS. A <c>PATCH</c> accepts the same fields, with the
///     certificate password in place of <c>PfxPasswordConfigured</c>. Every change needs a process
///     restart (see <see cref="RuntimeSettingsWriteResult.WebListenRestartRequiredNote" />).
/// </summary>
/// <param name="HttpPort">The plain HTTP port. Defaults to 80.</param>
/// <param name="HttpsPort">The HTTPS port. Defaults to 443.</param>
/// <param name="ListenAddresses">
///     The IP addresses both ports bind, as configured; empty (the default) means every address.
/// </param>
/// <param name="Certificate">
///     The server certificate configuration, in the shape used for DNS-over-TLS and
///     DNS-over-QUIC, since all three bind a <c>TlsCertificateOptions</c>.
/// </param>
public record WebListenSettingsDetail(
    int HttpPort,
    int HttpsPort,
    IReadOnlyList<string> ListenAddresses,
    TlsCertificateSettingsDetail Certificate
);
