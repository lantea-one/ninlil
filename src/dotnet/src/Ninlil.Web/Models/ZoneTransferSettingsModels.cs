// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the zone transfer settings (<c>ZoneTransferOptions</c>) that
///     <c>GET /api/zone-transfer-settings</c> returns: how the AXFR and IXFR responses the server
///     sends to secondaries are split into messages. A <c>PATCH</c> to the same endpoint accepts
///     the same field.
/// </summary>
/// <param name="RecordsPerMessage">The most records put in one transfer message. Defaults to 100.</param>
public record ZoneTransferSettingsDetail(int RecordsPerMessage);
