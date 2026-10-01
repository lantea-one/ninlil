using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Forwarders;

/// <summary>
///     This class supplies the Swagger example for a page of <see cref="ForwarderDetail" />, as
///     returned by <see cref="Controllers.ForwardersController.List" />.
/// </summary>
public class ForwarderDetailPagedResultExample : IExamplesProvider<PagedResult<ForwarderDetail>>
{
    /// <summary>
    ///     This method returns a page holding the three forwarders from
    ///     <see cref="ForwarderDetailExample" />, in <see cref="ForwarderDetail.SortOrder" />
    ///     order, as the list endpoint returns them.
    /// </summary>
    public PagedResult<ForwarderDetail> GetExamples() =>
        new(
            [
                new ForwarderDetail(
                    Guid.Parse("a1b2c3d4-1213-4415-8617-a1b2c3d4e5f1"),
                    "Cloudflare (Plain)",
                    "1.1.1.1",
                    UpstreamForwarderProtocolEnum.Plain,
                    0,
                    false,
                    "Cloudflare's public resolver over plain UDP/TCP.",
                    DateTimeOffset.Parse("2026-06-15T10:00:00+00:00"),
                    null
                ),
                new ForwarderDetail(
                    Guid.Parse("b2c3d4e5-2324-4526-9728-b2c3d4e5f6a2"),
                    "Cloudflare (DNS-over-TLS)",
                    "tls://one.one.one.one",
                    UpstreamForwarderProtocolEnum.Tls,
                    1,
                    false,
                    "Cloudflare's public resolver over DNS-over-TLS.",
                    DateTimeOffset.Parse("2026-06-15T10:01:00+00:00"),
                    null
                ),
                new ForwarderDetail(
                    Guid.Parse("c3d4e5f6-3435-4637-a839-c3d4e5f6a1b3"),
                    "Cloudflare (DNS-over-HTTPS)",
                    "https://cloudflare-dns.com/dns-query",
                    UpstreamForwarderProtocolEnum.Https,
                    2,
                    false,
                    "Cloudflare's public resolver over DNS-over-HTTPS.",
                    DateTimeOffset.Parse("2026-06-15T10:02:00+00:00"),
                    null
                ),
            ],
            0,
            50,
            3
        );
}
