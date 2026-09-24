using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebhookSubscriptionCreateResponse" />, referenced by
///     <see cref="Controllers.WebhooksController.Create" />.
/// </summary>
public class WebhookSubscriptionCreateResponseExample
    : IExamplesProvider<WebhookSubscriptionCreateResponse>
{
    /// <summary>
    ///     This method returns a freshly created subscription alongside its own plaintext auth
    ///     credential (echoing what the caller already supplied, per
    ///     <see cref="WebhookSubscriptionWriteRequest" />'s own doc comment on why that field has
    ///     no reveal-once problem) and its freshly generated, plaintext HMAC signing secret. Both
    ///     the bearer token and the HMAC secret are obviously-fake, base64-looking placeholder
    ///     values, not values shaped to look like a real, genuinely-leakable secret -- this
    ///     response is the one and only place a real subscription's own HMAC secret is ever
    ///     returned, per <see cref="WebhookSubscriptionDetail" />'s own doc comment, so a
    ///     documented example must never look like it could be mistaken for one.
    /// </summary>
    public WebhookSubscriptionCreateResponse GetExamples() =>
        new(
            new WebhookSubscriptionDetail(
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
            ),
            new Dictionary<string, string> { ["BearerToken"] = "example-not-a-real-bearer-token" },
            "ZXhhbXBsZS13ZWJob29rLWhtYWMtc2lnbmluZy1zZWNyZXQtbm90LXJlYWw="
        );
}
