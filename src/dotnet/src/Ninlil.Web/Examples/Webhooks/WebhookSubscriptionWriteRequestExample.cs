using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the Swagger example for <see cref="WebhookSubscriptionWriteRequest" />,
///     the request body of <see cref="Controllers.WebhooksController.Replace" />.
/// </summary>
public class WebhookSubscriptionWriteRequestExample
    : IExamplesProvider<WebhookSubscriptionWriteRequest>
{
    /// <summary>
    ///     This method returns a full replacement that adds a third event type to the two in
    ///     <see cref="WebhookSubscriptionDetailExample" />; the list is replaced, not merged. There
    ///     is no signing secret field (see <see cref="WebhookSubscriptionWriteRequest" />).
    /// </summary>
    public WebhookSubscriptionWriteRequest GetExamples() =>
        new(
            "zone-change-notifier",
            "https://ops.example.com/webhooks/ninlil",
            ["DnsZoneChanged", "TrustAnchorRollover", "DnssecZoneResigningCompleted"],
            WebhookAuthModeEnum.Bearer,
            new Dictionary<string, string> { ["BearerToken"] = "example-not-a-real-bearer-token" },
            5,
            false,
            "Notifies the on-call ops channel whenever a zone changes, a trust anchor rolls over, or a zone is re-signed."
        );
}
