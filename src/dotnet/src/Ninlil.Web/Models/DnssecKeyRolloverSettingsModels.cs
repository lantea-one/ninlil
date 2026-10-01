// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the key rollover timing (<c>DnssecKeyRolloverOptions</c>)
///     that <c>GET /api/dnssec-key-rollover-settings</c> returns. <c>DnssecKeyRolloverBackgroundService</c>
///     uses it to step the keys of every locally signed zone through the RFC 6781 §4.1.1.1 and
///     §4.1.2 rollovers. A <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="PropagationDelay">The assumed time for a zone change to reach all of the zone's authoritative servers, added to each rollover wait. Defaults to 1 hour.</param>
/// <param name="ExternalParentDsTtl">The assumed TTL of the DS record at a parent zone this server does not host, used in KSK rollovers. Defaults to 1 day.</param>
/// <param name="PollInterval">How often the background service checks for a rollover step that is due. Defaults to 15 minutes.</param>
public record DnssecKeyRolloverSettingsDetail(
    TimeSpan PropagationDelay,
    TimeSpan ExternalParentDsTtl,
    TimeSpan PollInterval
);
