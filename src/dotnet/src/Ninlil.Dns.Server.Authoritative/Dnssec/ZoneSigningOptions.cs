// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneSigning</c> section of the
///     application's configuration and controls the timing parameters
///     <see cref="ZoneSigningService" /> signs with, and <see cref="DnssecResigningBackgroundService" />
///     re-signs against.
/// </summary>
public class ZoneSigningOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneSigning";

    /// <summary>
    ///     This property contains how long a freshly computed signature remains valid -- the gap
    ///     between an <see cref="Common.Wire.Records.RrSigRecordData.SignatureInception" /> and
    ///     its own <see cref="Common.Wire.Records.RrSigRecordData.SignatureExpiration" />. 30 days
    ///     is a common, conservative default across real-world DNSSEC deployments -- long enough
    ///     that a brief signing-pipeline outage cannot expire a zone's signatures before anyone
    ///     notices, short enough that a compromised or retired key cannot forge indefinitely long
    ///     validity into the future.
    /// </summary>
    public TimeSpan SignatureValidityPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how far *before* the actual signing time a signature's
    ///     <see cref="Common.Wire.Records.RrSigRecordData.SignatureInception" /> is deliberately
    ///     backdated, tolerating a validator's own clock running slightly behind this server's.
    /// </summary>
    public TimeSpan InceptionOffset { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains how long before a zone's *earliest*-expiring signature actually
    ///     expires that <see cref="DnssecResigningBackgroundService" /> proactively re-signs it --
    ///     needed because a zone's data can sit completely unchanged for the entire
    ///     <see cref="SignatureValidityPeriod" />, yet its signatures still age out on their own
    ///     regardless, independent of <c>DnsZoneChangedEvent</c> ever firing again.
    /// </summary>
    public TimeSpan RefreshBeforeExpiration { get; set; } = TimeSpan.FromDays(5);

    /// <summary>
    ///     This property contains how often <see cref="DnssecResigningBackgroundService" />'s
    ///     background loop wakes up to check whether any signed zone is due for the proactive
    ///     re-sign <see cref="RefreshBeforeExpiration" /> describes.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);
}
