using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.FilterLists;

/// <summary>
///     This class supplies the Swagger examples for <see cref="FilterListWriteRequest" />, the
///     request body of <see cref="Controllers.FilterListsController.Create" /> and
///     <see cref="Controllers.FilterListsController.Replace" />.
/// </summary>
public class FilterListWriteRequestExample : IMultipleExamplesProvider<FilterListWriteRequest>
{
    /// <summary>
    ///     This method returns a <see cref="DnsFilterListSourceEnum.Local" /> list, where
    ///     <see cref="FilterListWriteRequest.SubscriptionUrl" /> and
    ///     <see cref="FilterListWriteRequest.RefreshInterval" /> do not apply, and a
    ///     <see cref="DnsFilterListSourceEnum.Subscribed" /> list, whose entries are replaced from
    ///     that URL on each refresh.
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
