using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterLists;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="FilterListWriteRequest" />, referenced by
///     <see cref="Controllers.FilterListsController.Create" /> and
///     <see cref="Controllers.FilterListsController.Replace" />.
/// </summary>
public class FilterListWriteRequestExample : IMultipleExamplesProvider<FilterListWriteRequest>
{
    /// <summary>
    ///     This method returns two genuinely different list shapes -- a manually-curated
    ///     <see cref="DnsFilterListSourceEnum.Local" /> list (whose own
    ///     <see cref="FilterListWriteRequest.SubscriptionUrl" />/
    ///     <see cref="FilterListWriteRequest.RefreshInterval" /> are meaningless) and a
    ///     <see cref="DnsFilterListSourceEnum.Subscribed" /> list (whose own entries are instead
    ///     periodically replaced wholesale from that URL) -- so a caller reading this example sees
    ///     both of this field pair's conditional shapes in one place, rather than guessing which
    ///     applies.
    /// </summary>
    public IEnumerable<SwaggerExample<FilterListWriteRequest>> GetExamples() =>
        [
            new()
            {
                Name = "Local allowlist",
                Summary = "A manually-curated allowlist an operator edits directly.",
                Value = new FilterListWriteRequest(
                    "Internal Allowlist",
                    DnsFilterListKindEnum.Domain,
                    DnsFilterListActionEnum.Allow,
                    DnsFilterListSourceEnum.Local,
                    SubscriptionUrl: null,
                    RefreshInterval: null,
                    IsDisabled: false,
                    Comments: "Manually curated allowlist for internal services."
                ),
            },
            new()
            {
                Name = "Subscribed blocklist",
                Summary = "A hosts-file-format blocklist refreshed daily from a remote URL.",
                Value = new FilterListWriteRequest(
                    "Hagezi Pro Blocklist",
                    DnsFilterListKindEnum.Domain,
                    DnsFilterListActionEnum.Block,
                    DnsFilterListSourceEnum.Subscribed,
                    SubscriptionUrl: "https://raw.githubusercontent.com/hagezi/dns-blocklists/main/hosts/pro.txt",
                    RefreshInterval: TimeSpan.FromHours(24),
                    IsDisabled: false,
                    Comments: "Community-maintained ad/tracker blocklist, refreshed daily."
                ),
            },
        ];
}
