using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the Swagger example for
///     <see cref="WebhookSubscriptionCreateResponse" />, as returned by
///     <see cref="Controllers.WebhooksController.Create" />.
/// </summary>
public class WebhookSubscriptionCreateResponseExample
    : IExamplesProvider<WebhookSubscriptionCreateResponse>
{
    /// <summary>
    ///     This method returns a new subscription with its plaintext bearer token, echoed from the
    ///     request (see <see cref="WebhookSubscriptionWriteRequest" />), and its generated HMAC
    ///     signing secret. This response is the only place the signing secret is returned, so both
    ///     values are placeholders that cannot be mistaken for real secrets.
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
