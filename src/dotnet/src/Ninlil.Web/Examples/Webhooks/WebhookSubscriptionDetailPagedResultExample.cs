using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a page of
///     <see cref="WebhookSubscriptionDetail" />, referenced by
///     <see cref="Controllers.WebhooksController.List" />.
/// </summary>
public class WebhookSubscriptionDetailPagedResultExample
    : IExamplesProvider<PagedResult<WebhookSubscriptionDetail>>
{
    /// <summary>
    ///     This method returns one page of two subscriptions -- the same active,
    ///     bearer-authenticated subscription <see cref="WebhookSubscriptionDetailExample" />
    ///     already shows individually, alongside an unauthenticated, currently disabled one, so a
    ///     caller reading this example sees both <see cref="WebhookSubscriptionDetail.IsDisabled" />
    ///     shapes together.
    /// </summary>
    public PagedResult<WebhookSubscriptionDetail> GetExamples() =>
        new(
            [
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
                new WebhookSubscriptionDetail(
                    Guid.Parse("e5f6a7b8-9021-4b3c-a4d5-e6f7a8b9c1d2"),
                    "cloud-sync-failure-alerts",
                    "https://alerts.example.org/hooks/ninlil-cloud-sync",
                    ["CloudDnsZoneSyncFailed", "AcmeCertificateRenewalFailed"],
                    WebhookAuthModeEnum.None,
                    false,
                    3,
                    true,
                    "Paused while the alerting endpoint is being migrated.",
                    DateTimeOffset.Parse("2026-05-01T08:30:00+00:00"),
                    DateTimeOffset.Parse("2026-08-25T14:45:00+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
