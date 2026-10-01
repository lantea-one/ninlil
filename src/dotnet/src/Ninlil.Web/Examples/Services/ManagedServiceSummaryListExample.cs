using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the Swagger example for the response of
///     <see cref="Controllers.ServicesController.List" />.
/// </summary>
public class ManagedServiceSummaryListExample
    : IExamplesProvider<IReadOnlyList<ManagedServiceSummary>>
{
    /// <summary>
    ///     This method returns 8 of the 26 registered services: five transports, with DNS-over-QUIC
    ///     not running as on a system without <c>libmsquic</c>, and three background loops, so both
    ///     categories appear.
    /// </summary>
    public IReadOnlyList<ManagedServiceSummary> GetExamples() =>
        [
            new ManagedServiceSummary("plain-udp", ManagedServiceCategoryEnum.Transport, true),
            new ManagedServiceSummary("plain-tcp", ManagedServiceCategoryEnum.Transport, true),
            new ManagedServiceSummary("dns-over-tls", ManagedServiceCategoryEnum.Transport, true),
            new ManagedServiceSummary("dns-over-quic", ManagedServiceCategoryEnum.Transport, false),
            new ManagedServiceSummary("dns-over-https", ManagedServiceCategoryEnum.Transport, true),
            new ManagedServiceSummary(
                "query-processing-pipeline",
                ManagedServiceCategoryEnum.Background,
                true
            ),
            new ManagedServiceSummary(
                "dnssec-resigning",
                ManagedServiceCategoryEnum.Background,
                true
            ),
            new ManagedServiceSummary(
                "query-log-cleanup",
                ManagedServiceCategoryEnum.Background,
                true
            ),
        ];
}
