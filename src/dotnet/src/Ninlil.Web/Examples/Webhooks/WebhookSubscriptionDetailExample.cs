using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebhookSubscriptionDetail" />, referenced by
///     <see cref="Controllers.WebhooksController.Get" />.
/// </summary>
public class WebhookSubscriptionDetailExample : IExamplesProvider<WebhookSubscriptionDetail>
{
    /// <summary>
    ///     This method returns a subscription subscribed to two of the seven real, currently
    ///     delivery-eligible event types (see <see cref="DnsServerEventTypeRegistry" />'s own
    ///     doc comment for the full curated set), with a bearer-token auth mode and HMAC payload
    ///     signing enabled -- neither the bearer token itself nor the HMAC secret is ever
    ///     included here, only <see cref="WebhookSubscriptionDetail.HasHmacSigningSecret" />; see
    ///     <see cref="WebhookSubscriptionCreateResponseExample" /> for the one response shape
    ///     that reveals either.
    /// </summary>
    public WebhookSubscriptionDetail GetExamples() =>
        new(
            Guid.Parse("d4e5f6a7-8091-4a2b-93c4-d5e6f7a8b9c1"),
            "zone-change-notifier",
            "https://ops.example.com/webhooks/ninlil",
            ["DnsZoneChanged", "TrustAnchorRollover"],
            WebhookAuthModeEnum.Bearer,
            true,
            5,
            false,
            "Notifies the on-call ops channel whenever a zone changes or a trust anchor rolls over.",
            DateTimeOffset.Parse("2026-07-15T10:00:00+00:00"),
            null
        );
}
