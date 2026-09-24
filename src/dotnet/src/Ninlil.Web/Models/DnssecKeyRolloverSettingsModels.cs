// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnssecKeyRolloverOptions</c> -- the RFC 6781 §4.1.1.1/§4.1.2 key-rollover timing
///     <c>DnssecKeyRolloverBackgroundService</c> walks every locally signed zone's own keys
///     against. No secret material is involved, so this record's own fields are the exact same
///     ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record DnssecKeyRolloverSettingsDetail(
    TimeSpan PropagationDelay,
    TimeSpan ExternalParentDsTtl,
    TimeSpan PollInterval
);
