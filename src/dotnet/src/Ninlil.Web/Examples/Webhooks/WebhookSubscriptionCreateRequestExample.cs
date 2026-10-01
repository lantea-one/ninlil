using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the Swagger example for <see cref="WebhookSubscriptionCreateRequest" />,
///     the request body of <see cref="Controllers.WebhooksController.Create" />.
/// </summary>
public class WebhookSubscriptionCreateRequestExample
    : IExamplesProvider<WebhookSubscriptionCreateRequest>
{
    /// <summary>
    ///     This method returns a request with a placeholder bearer token and HMAC signing on but no
    ///     signing secret, so the server generates one, the usual case (as in
    ///     <see cref="TsigKeys.TsigKeyCreateRequestExample" />).
    /// </summary>
    public WebhookSubscriptionCreateRequest GetExamples() =>
        new(
            "zone-change-notifier",
            "https://ops.example.com/webhooks/ninlil",
            ["DnsZoneChanged", "TrustAnchorRollover"],
            WebhookAuthModeEnum.Bearer,
            new Dictionary<string, string> { ["BearerToken"] = "example-not-a-real-bearer-token" },
            true,
            null,
            5,
            false,
            "Notifies the on-call ops channel whenever a zone changes or a trust anchor rolls over."
        );
}
