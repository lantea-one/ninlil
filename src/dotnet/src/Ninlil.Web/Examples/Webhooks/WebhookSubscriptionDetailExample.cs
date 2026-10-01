using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.Events.Broker;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the Swagger example for <see cref="WebhookSubscriptionDetail" />, as
///     returned by <see cref="Controllers.WebhooksController.Get" />.
/// </summary>
public class WebhookSubscriptionDetailExample : IExamplesProvider<WebhookSubscriptionDetail>
{
    /// <summary>
    ///     This method returns a subscription to two event types from
    ///     <see cref="DnsServerEventTypeRegistry" />, with bearer authentication and HMAC signing.
    ///     Neither secret is included, only
    ///     <see cref="WebhookSubscriptionDetail.HasHmacSigningSecret" />;
    ///     <see cref="WebhookSubscriptionCreateResponseExample" /> is the one response that shows
    ///     them.
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
