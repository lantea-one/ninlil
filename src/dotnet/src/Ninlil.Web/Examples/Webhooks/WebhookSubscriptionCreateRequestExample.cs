using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="WebhookSubscriptionCreateRequest" />, referenced by
///     <see cref="Controllers.WebhooksController.Create" />.
/// </summary>
public class WebhookSubscriptionCreateRequestExample
    : IExamplesProvider<WebhookSubscriptionCreateRequest>
{
    /// <summary>
    ///     This method returns a request that supplies its own bearer token (an obviously-fake
    ///     placeholder, never a value shaped to look like a real, genuinely-leakable credential)
    ///     but leaves <see cref="WebhookSubscriptionCreateRequest.HmacSigningSecret" /> unset,
    ///     letting the server generate a cryptographically random one instead -- the more common
    ///     case, mirroring <see cref="TsigKeys.TsigKeyCreateRequestExample" />'s own identical
    ///     choice for its own server-generatable secret.
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
