using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterLists;

/// <summary>
///     This class supplies the Swagger examples for <see cref="FilterListDetail" />, as returned by
///     <see cref="Controllers.FilterListsController.Get" /> and
///     <see cref="Controllers.FilterListsController.Create" />.
/// </summary>
public class FilterListDetailExample : IMultipleExamplesProvider<FilterListDetail>
{
    /// <summary>
    ///     This method returns the two lists <see cref="FilterListWriteRequestExample" /> submits,
    ///     as the server reports them. The subscribed list has a
    ///     <see cref="FilterListDetail.LastFetchedAtUtc" /> from its last refresh; the local list's
    ///     is <see langword="null" />, since it is never fetched.
    /// </summary>
    public IEnumerable<SwaggerExample<FilterListDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Local allowlist",
                Summary = "A manually-curated allowlist an operator edits directly.",
                Value = new FilterListDetail(
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
            },
            new()
            {
                Name = "Subscribed blocklist",
                Summary = "A hosts-file-format blocklist refreshed daily from a remote URL.",
                Value = new FilterListDetail(
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
            },
        ];
}
