// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>ZoneSigningOptions</c> -- the timing parameters <c>ZoneSigningService</c> signs with and
///     <c>DnssecResigningBackgroundService</c> re-signs against. No secret material is involved,
///     so this record's own fields are the exact same ones a <c>PATCH</c> to the same endpoint
///     accepts.
/// </summary>
/// <param name="SignatureValidityPeriod">How long a freshly computed signature remains valid.</param>
/// <param name="InceptionOffset">How far before the actual signing time a signature's own inception is deliberately backdated.</param>
/// <param name="RefreshBeforeExpiration">How long before a zone's earliest-expiring signature actually expires that a proactive re-sign runs.</param>
/// <param name="PollInterval">How often the proactive re-signing background loop wakes up to check whether any signed zone is due.</param>
public record ZoneSigningSettingsDetail(
    TimeSpan SignatureValidityPeriod,
    TimeSpan InceptionOffset,
    TimeSpan RefreshBeforeExpiration,
    TimeSpan PollInterval
);
