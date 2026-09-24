using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsRewriteWriteRequest" />, referenced by both
///     <see cref="Controllers.DnsRewritesController.Create" /> and
///     <see cref="Controllers.DnsRewritesController.Replace" />.
/// </summary>
public class DnsRewriteWriteRequestExample : IExamplesProvider<DnsRewriteWriteRequest>
{
    /// <summary>
    ///     This method returns a plain, exact-domain, A-target rewrite -- the single most common
    ///     shape this feature exists for, an internal hostname pointed at a LAN address.
    /// </summary>
    public DnsRewriteWriteRequest GetExamples() =>
        new(
            "printer.internal.example.com",
            "192.0.2.50",
            IsDisabled: false,
            Comments: "Office printer, moved off DHCP onto a static reservation."
        );
}
