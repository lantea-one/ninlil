using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Forwarders;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="ForwarderWriteRequest" />, referenced by
///     <see cref="Controllers.ForwardersController.Create" /> and
///     <see cref="Controllers.ForwardersController.Replace" />.
/// </summary>
public class ForwarderWriteRequestExample : IMultipleExamplesProvider<ForwarderWriteRequest>
{
    /// <summary>
    ///     This method returns three genuinely different definition-string shapes -- a bare
    ///     address (plain UDP/TCP), a <c>tls://</c> URL (DNS-over-TLS), and an <c>https://</c> URL
    ///     (DNS-over-HTTPS) -- all pointed at Cloudflare's real, publicly-documented resolver
    ///     endpoints, the same real-world addresses this project's own
    ///     <c>Server.Forwarding.ClientProtocols</c> test suite already uses, rather than an
    ///     invented-looking placeholder. <c>ForwarderWriteRequest.DefinitionString</c>'s own doc
    ///     comment lists the two remaining supported forms (<c>quic://</c>, <c>sdns://</c>).
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
