using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the Swagger example for <see cref="DnsRewriteDetail" />, as returned by
///     <see cref="Controllers.DnsRewritesController.Get" /> and
///     <see cref="Controllers.DnsRewritesController.Create" />.
/// </summary>
public class DnsRewriteDetailExample : IExamplesProvider<DnsRewriteDetail>
{
    /// <summary>
    ///     This method returns the rewrite <see cref="DnsRewriteWriteRequestExample" /> submits, as
    ///     the server reports it. <see cref="DnsRewriteDetail.TargetKind" /> is computed when the
    ///     response is built; an IPv4 target classifies as <c>A</c>.
    /// </summary>
    public DnsRewriteDetail GetExamples() =>
        new(
            Guid.Parse("6f1e2d3c-4b5a-4978-8f01-9a2b3c4d5e6f"),
            "printer.internal.example.com",
            "192.0.2.50",
            DnsRewriteTargetKindEnum.A,
            false,
            "Office printer, moved off DHCP onto a static reservation.",
            DateTimeOffset.Parse("2026-08-31T09:00:00+00:00"),
            null
        );
}
