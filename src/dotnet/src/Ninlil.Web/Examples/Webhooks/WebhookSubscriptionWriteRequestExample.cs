using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebhookSubscriptionWriteRequest" />, referenced by
///     <see cref="Controllers.WebhooksController.Replace" />.
/// </summary>
public class WebhookSubscriptionWriteRequestExample
    : IExamplesProvider<WebhookSubscriptionWriteRequest>
{
    /// <summary>
    ///     This method returns a full replacement of a subscription's own metadata and auth
    ///     credentials -- adding a third event type to the two <see cref="WebhookSubscriptionDetailExample" />
    ///     already shows, since <see cref="WebhookSubscriptionWriteRequest.EventTypeNames" /> is
    ///     replaced wholesale, not merged. This subscription's own HMAC signing secret has no
    ///     field here at all; see <see cref="WebhookSubscriptionWriteRequest" />'s own doc comment
    ///     for why.
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
