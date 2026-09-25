// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Dnssec;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneSigning</c> section of the
///     application's configuration and sets the signature timing used by
///     <see cref="ZoneSigningService" /> and the re-signing schedule of
///     <see cref="DnssecResigningBackgroundService" />. Both read it on each use.
/// </summary>
public class ZoneSigningOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneSigning";

    /// <summary>
    ///     This property contains how long after signing a signature expires (measured from the
    ///     signing time; the inception is backdated separately by <see cref="InceptionOffset" />).
    ///     30 days is a common default: long enough that a short signing outage does not expire
    ///     the zone, short enough that a stolen key's signatures do not stay valid for long.
    /// </summary>
    public TimeSpan SignatureValidityPeriod { get; set; } = TimeSpan.FromDays(30);

    /// <summary>
    ///     This property contains how far before the signing time a signature's
    ///     <see cref="Common.Wire.Records.RrSigRecordData.SignatureInception" /> is set, so a
    ///     validator whose clock runs slightly behind still accepts it.
    /// </summary>
    public TimeSpan InceptionOffset { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    ///     This property contains how long before its signatures expire a zone is re-signed by
    ///     <see cref="DnssecResigningBackgroundService" />. A zone that never changes would
    ///     otherwise let its signatures expire.
    /// </summary>
    public TimeSpan RefreshBeforeExpiration { get; set; } = TimeSpan.FromDays(5);

    /// <summary>
    ///     This property contains how often <see cref="DnssecResigningBackgroundService" /> checks
    ///     whether any zone is due for re-signing.
    /// </summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromHours(6);
}
