// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the zone signing timing (<c>ZoneSigningOptions</c>) that
///     <c>GET /api/zone-signing-settings</c> returns: what <c>ZoneSigningService</c> signs with
///     and <c>DnssecResigningBackgroundService</c> re-signs by. A <c>PATCH</c> to the same
///     endpoint accepts the same fields.
/// </summary>
/// <param name="SignatureValidityPeriod">How long a new signature stays valid. Defaults to 30 days.</param>
/// <param name="InceptionOffset">How far before the signing time a signature's inception is backdated, to allow for clock skew. Defaults to 1 hour.</param>
/// <param name="RefreshBeforeExpiration">How long before a zone's earliest signature expires the zone is re-signed. Defaults to 5 days.</param>
/// <param name="PollInterval">How often the re-signing loop checks whether a signed zone is due. Defaults to 6 hours.</param>
public record ZoneSigningSettingsDetail(
    TimeSpan SignatureValidityPeriod,
    TimeSpan InceptionOffset,
    TimeSpan RefreshBeforeExpiration,
    TimeSpan PollInterval
);
