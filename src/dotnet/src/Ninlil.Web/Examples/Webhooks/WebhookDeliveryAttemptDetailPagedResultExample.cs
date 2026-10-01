using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Webhooks;

/// <summary>
///     This class supplies the Swagger example for a page of
///     <see cref="WebhookDeliveryAttemptDetail" />, as returned by
///     <see cref="Controllers.WebhooksController.ListDeliveryAttempts" />.
/// </summary>
public class WebhookDeliveryAttemptDetailPagedResultExample
    : IExamplesProvider<PagedResult<WebhookDeliveryAttemptDetail>>
{
    /// <summary>
    ///     This method returns two deliveries for the subscription in
    ///     <see cref="WebhookSubscriptionDetailExample" />: one still pending after two failures
    ///     (the last a 503), waiting for its next retry, and one that succeeded on its first
    ///     attempt.
    /// </summary>
    public PagedResult<WebhookDeliveryAttemptDetail> GetExamples() =>
        new(
            [
                new WebhookDeliveryAttemptDetail(
                    Guid.Parse("f1a2b3c4-5d6e-4f70-8a1b-2c3d4e5f6a7b"),
                    Guid.Parse("d4e5f6a7-8091-4a2b-93c4-d5e6f7a8b9c1"),
                    "zone-change-notifier",
                    "DnsZoneChanged",
                    """{"eventType":"DnsZoneChanged","zoneId":"11111111-1111-1111-1111-111111111111"}""",
                    WebhookDeliveryAttemptStatusEnum.Pending,
                    2,
                    5,
                    DateTimeOffset.Parse("2026-09-11T08:15:32+00:00"),
                    DateTimeOffset.Parse("2026-09-11T08:16:04+00:00"),
                    503,
                    "The subscriber's endpoint returned HTTP 503 Service Unavailable.",
                    null,
                    DateTimeOffset.Parse("2026-09-11T08:15:30+00:00")
                ),
                new WebhookDeliveryAttemptDetail(
                    Guid.Parse("a2b3c4d5-6e7f-4081-9b2c-3d4e5f6a7b8c"),
                    Guid.Parse("d4e5f6a7-8091-4a2b-93c4-d5e6f7a8b9c1"),
                    "zone-change-notifier",
                    "TrustAnchorRollover",
                    """{"eventType":"TrustAnchorRollover","zone":"example.com"}""",
                    WebhookDeliveryAttemptStatusEnum.Succeeded,
                    1,
                    5,
                    DateTimeOffset.Parse("2026-09-10T22:03:11+00:00"),
                    null,
                    200,
                    null,
                    DateTimeOffset.Parse("2026-09-10T22:03:11+00:00"),
                    DateTimeOffset.Parse("2026-09-10T22:03:11+00:00")
                ),
            ],
            0,
            50,
            2
        );
}
