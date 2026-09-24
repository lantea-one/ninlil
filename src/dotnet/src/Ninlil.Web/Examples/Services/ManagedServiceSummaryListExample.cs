using Ninlil.Dns.Server.Core.Services;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.Services;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.ServicesController.List" />.
/// </summary>
public class ManagedServiceSummaryListExample
    : IExamplesProvider<IReadOnlyList<ManagedServiceSummary>>
{
    /// <summary>
    ///     This method returns a representative subset of this server's real, full service list --
    ///     every transport this project ships (one of them, DNS-over-QUIC, not currently running,
    ///     the way it would genuinely read on a platform with no <c>libmsquic</c> available) plus a
    ///     handful of background maintenance loops -- not the complete set (26 entries, all
    ///     illustrated the same way), since this sample is meant to show the response shape and
    ///     both category values, not enumerate every service by name.
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
