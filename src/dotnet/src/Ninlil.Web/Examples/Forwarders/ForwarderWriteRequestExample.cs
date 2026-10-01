using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Forwarders;

/// <summary>
///     This class supplies the Swagger examples for <see cref="ForwarderWriteRequest" />, the
///     request body of <see cref="Controllers.ForwardersController.Create" /> and
///     <see cref="Controllers.ForwardersController.Replace" />.
/// </summary>
public class ForwarderWriteRequestExample : IMultipleExamplesProvider<ForwarderWriteRequest>
{
    /// <summary>
    ///     This method returns three definition forms, a bare address (plain UDP and TCP), a
    ///     <c>tls://</c> URL and an <c>https://</c> URL, all for Cloudflare's public resolver,
    ///     which the <c>Server.Forwarding.ClientProtocols</c> tests also use. The <c>quic://</c>
    ///     and <c>sdns://</c> forms are described on <c>ForwarderWriteRequest.DefinitionString</c>.
    /// </summary>
    public IEnumerable<SwaggerExample<ForwarderWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Plain UDP/TCP",
                Summary = "A bare address, the conventional no-scheme plain-DNS form.",
                Value = new ForwarderWriteRequest(
                    "Cloudflare (Plain)",
                    "1.1.1.1",
                    SortOrder: 0,
                    IsDisabled: false,
                    Comments: "Cloudflare's public resolver over plain UDP/TCP."
                ),
            },
            new()
            {
                Name = "DNS-over-TLS",
                Summary =
                    "An RFC 7858 tls:// URL, resolved and validated against its own hostname's certificate.",
                Value = new ForwarderWriteRequest(
                    "Cloudflare (DNS-over-TLS)",
                    "tls://one.one.one.one",
                    SortOrder: 1,
                    IsDisabled: false,
                    Comments: "Cloudflare's public resolver over DNS-over-TLS."
                ),
            },
            new()
            {
                Name = "DNS-over-HTTPS",
                Summary = "An RFC 8484 https:// URL naming the full DoH query endpoint.",
                Value = new ForwarderWriteRequest(
                    "Cloudflare (DNS-over-HTTPS)",
                    "https://cloudflare-dns.com/dns-query",
                    SortOrder: 2,
                    IsDisabled: false,
                    Comments: "Cloudflare's public resolver over DNS-over-HTTPS."
                ),
            },
        ];
}
