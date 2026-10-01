using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterLists;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="FilterListDetail" />, as
///     returned by <see cref="Controllers.FilterListsController.List" />.
/// </summary>
public class FilterListDetailPagedResultExample : IExamplesProvider<PagedResult<FilterListDetail>>
{
    /// <summary>
    ///     This method returns a page holding the local allowlist and the subscribed blocklist from
    ///     <see cref="FilterListDetailExample" />.
    /// </summary>
    public PagedResult<FilterListDetail> GetExamples() =>
        new(
            [
                new FilterListDetail(
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "Internal Allowlist",
                    DnsFilterListKindEnum.Domain,
                    DnsFilterListActionEnum.Allow,
                    DnsFilterListSourceEnum.Local,
                    null,
                    null,
                    null,
                    false,
                    "Manually curated allowlist for internal services.",
                    DateTimeOffset.Parse("2026-07-01T08:00:00+00:00"),
                    null
                ),
                new FilterListDetail(
                    Guid.Parse("8f14e45f-ceea-4b7f-9e33-1f7c2f4a2222"),
                    "Hagezi Pro Blocklist",
                    DnsFilterListKindEnum.Domain,
                    DnsFilterListActionEnum.Block,
                    DnsFilterListSourceEnum.Subscribed,
                    "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/hosts/pro.txt",
                    TimeSpan.FromHours(24),
                    DateTimeOffset.Parse("2026-08-31T02:00:00+00:00"),
                    false,
                    "Community-maintained ad/tracker blocklist, refreshed daily.",
                    DateTimeOffset.Parse("2026-07-01T08:05:00+00:00"),
                    DateTimeOffset.Parse("2026-08-31T02:00:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
