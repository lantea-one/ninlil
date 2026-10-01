using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterListEntries;

/// <summary>
///     This class supplies the Swagger examples for <see cref="FilterListEntryDetail" />, as
///     returned by <see cref="Controllers.FilterListEntriesController.Get" /> and
///     <see cref="Controllers.FilterListEntriesController.Create" />.
/// </summary>
public class FilterListEntryDetailExample : IMultipleExamplesProvider<FilterListEntryDetail>
{
    /// <summary>
    ///     This method returns the three entries <see cref="FilterListEntryWriteRequestExample" />
    ///     submits, as the server reports them, plus an <c>IsException</c> entry of the kind only a
    ///     subscribed list's refresh creates. All four carry the identifier of the "Internal
    ///     Allowlist" from <see cref="FilterLists.FilterListDetailExample" />.
    /// </summary>
    public IEnumerable<SwaggerExample<FilterListEntryDetail>> GetExamples() =>
        [
            new()
            {
                Name = "Exact domain",
                Summary = "Matches the exact domain and every one of its subdomains.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("1c2d3e4f-5a6b-4c7d-8e9f-0a1b2c3d4e01"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "ads.example.net",
                    "Known ad-network domain.",
                    DnsFilterEntryKindEnum.Domain,
                    DateTimeOffset.Parse("2026-07-01T08:10:00+00:00")
                ),
            },
            new()
            {
                Name = "Wildcard glob",
                Summary =
                    "AdGuard Home-style '*'/'?' glob syntax, matched against the full queried name.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("2d3e4f5a-6b7c-4d8e-9f0a-1b2c3d4e5f02"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "*.ads.example.net",
                    "Matches every ad-network subdomain, however deep.",
                    DnsFilterEntryKindEnum.Wildcard,
                    DateTimeOffset.Parse("2026-07-01T08:11:00+00:00")
                ),
            },
            new()
            {
                Name = "Regular expression",
                Summary = "Pi-hole-style regex syntax, matched against the full queried name.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("3e4f5a6b-7c8d-4e9f-0a1b-2c3d4e5f6a03"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    """^track\d+\.example\.net$""",
                    "Matches every numbered tracking subdomain.",
                    DnsFilterEntryKindEnum.Regex,
                    DateTimeOffset.Parse("2026-07-01T08:12:00+00:00")
                ),
            },
            new()
            {
                Name = "Subscribed-list exception",
                Summary =
                    "An adblock '@@||cdn.ads.example.net^' line from a subscribed list: lets this name "
                    + "(and its subdomains) through despite the same list's broader rule.",
                Value = new FilterListEntryDetail(
                    Guid.Parse("4f5a6b7c-8d9e-4f0a-1b2c-3d4e5f6a7b04"),
                    Guid.Parse("3f2504e0-4f89-4f6a-b8c1-f1a5e3a1b111"),
                    "cdn.ads.example.net",
                    null,
                    DnsFilterEntryKindEnum.Domain,
                    DateTimeOffset.Parse("2026-07-01T08:13:00+00:00"),
                    IsException: true
                ),
            },
        ];
}
