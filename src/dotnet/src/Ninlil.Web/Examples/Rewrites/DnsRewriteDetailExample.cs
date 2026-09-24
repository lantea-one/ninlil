using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="DnsRewriteDetail" />, referenced by
///     <see cref="Controllers.DnsRewritesController.Get" /> and
///     <see cref="Controllers.DnsRewritesController.Create" />.
/// </summary>
public class DnsRewriteDetailExample : IExamplesProvider<DnsRewriteDetail>
{
    /// <summary>
    ///     This method returns the same rewrite <see cref="DnsRewriteWriteRequestExample" />
    ///     submits, as this server would report it back -- <see cref="DnsRewriteDetail.TargetKind" />
    ///     is computed fresh at read time, never stored, so this example shows exactly what that
    ///     computation would produce for an IPv4-literal target.
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
