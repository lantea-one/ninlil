// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the zone transfer client settings (<c>ZoneTransferClientOptions</c>) that
///     <c>GET /api/zone-transfer-client-settings</c> returns: how the server, as a secondary,
///     pulls AXFR and IXFR transfers from a primary. A <c>PATCH</c> to the same endpoint accepts
///     the same fields.
/// </summary>
/// <param name="PrimaryPort">The port plain transfers and SOA checks connect to on a primary. Defaults to 53.</param>
/// <param name="ConnectTimeout">How long connecting to a primary may take. Defaults to 5 seconds.</param>
/// <param name="ReadTimeout">How long to wait for each part of a transfer response. Defaults to 10 seconds.</param>
/// <param name="SoaQueryTimeout">How long the SOA serial check may take. Defaults to 3 seconds.</param>
public record ZoneTransferClientSettingsDetail(
    int PrimaryPort,
    TimeSpan ConnectTimeout,
    TimeSpan ReadTimeout,
    TimeSpan SoaQueryTimeout
);
