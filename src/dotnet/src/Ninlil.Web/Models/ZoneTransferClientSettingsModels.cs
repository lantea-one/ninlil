// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ZoneTransferClientOptions</c> -- how this server pulls AXFR/IXFR transfers from a
///     primary as a secondary. This record's own fields are the exact same ones a <c>PATCH</c> to
///     the same endpoint accepts.
/// </summary>
public record ZoneTransferClientSettingsDetail(
    int PrimaryPort,
    TimeSpan ConnectTimeout,
    TimeSpan ReadTimeout,
    TimeSpan SoaQueryTimeout
);
