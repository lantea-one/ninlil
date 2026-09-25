// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:DnssecKeyRollover</c> section of the
///     application's configuration and sets the timing <see cref="DnssecKeyRolloverBackgroundService" />
///     uses to move keys through RFC 6781 rollovers (§4.1.1.1 Pre-Publish for a ZSK, §4.1.2
///     Double-Signature for a KSK). It is read on each loop iteration.
/// </summary>
public class DnssecKeyRolloverOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:DnssecKeyRollover";

    /// <summary>
    ///     This property contains the assumed time for a zone change to reach all of the zone's
    ///     authoritative servers: the propagation delay RFC 6781 adds to each rollover wait on top
    ///     of the relevant TTL. A single server has none, but the default of one hour allows for
    ///     slow secondaries or cloud sync.
    /// </summary>
    public TimeSpan PropagationDelay { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains the assumed TTL of the DS record at an external parent zone,
    ///     used only in a <see cref="Common.Enum.DnssecKeyRoleEnum.Ksk" /> rollover whose parent
    ///     this server does not host; the server cannot look it up. A parent hosted here uses the
    ///     3600-second TTL <c>ZoneSigningService.ComputeDelegationDsRecordsAsync</c> writes.
    /// </summary>
    public TimeSpan ExternalParentDsTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    ///     This property contains how often <see cref="DnssecKeyRolloverBackgroundService" /> checks
    ///     for a rollover step that is due. It is shorter than the 6-hour default of
    ///     <c>ZoneSigningOptions.PollInterval</c> because a rollover wait can be a couple of hours
    ///     (a one-hour DNSKEY TTL plus propagation delay), and a slow poll would dominate it.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(15);
}
