using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsRewriteWriteRequest" />, the
///     request body of <see cref="Controllers.DnsRewritesController.Create" /> and
///     <see cref="Controllers.DnsRewritesController.Replace" />.
/// </summary>
public class DnsRewriteWriteRequestExample : IExamplesProvider<DnsRewriteWriteRequest>
{
    /// <summary>
    ///     This method returns the most common kind of rewrite: an internal host name pointed at a
    ///     LAN address.
    /// </summary>
    public DnsRewriteWriteRequest GetExamples() =>
        new(
            "printer.internal.example.com",
            "192.0.2.50",
            IsDisabled: false,
            Comments: "Office printer, moved off DHCP onto a static reservation."
        );
}
