using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Rewrites;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="DnsRewriteDetail" />, referenced by
///     <see cref="Controllers.DnsRewritesController.List" />.
/// </summary>
public class DnsRewriteDetailPagedResultExample : IExamplesProvider<PagedResult<DnsRewriteDetail>>
{
    /// <summary>
    ///     This method returns one page of two rewrites -- one exact, A-target rewrite, and one
    ///     CNAME-target wildcard rewrite, so a caller reading this example can see both classified
    ///     target kinds and both domain-matching shapes in a single, small response.
    /// </summary>
    public PagedResult<DnsRewriteDetail> GetExamples() =>
        new(
            [
                new DnsRewriteDetail(
                    Guid.Parse("6f1e2d3c-4b5a-4978-8f01-9a2b3c4d5e6f"),
                    "printer.internal.example.com",
                    "192.0.2.50",
                    DnsRewriteTargetKindEnum.A,
                    false,
                    "Office printer, moved off DHCP onto a static reservation.",
                    DateTimeOffset.Parse("2026-08-31T09:00:00+00:00"),
                    null
                ),
                new DnsRewriteDetail(
                    Guid.Parse("9c8b7a6f-5e4d-43c2-b1a0-908f7e6d5c4b"),
                    "*.dev.internal.example.com",
                    "internal-gateway.example.com",
                    DnsRewriteTargetKindEnum.Cname,
                    false,
                    "Every dev subdomain routes through the same internal gateway.",
                    DateTimeOffset.Parse("2026-08-15T14:30:00+00:00"),
                    DateTimeOffset.Parse("2026-08-20T11:05:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
