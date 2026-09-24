// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneTransfer</c> section of the
///     application's configuration and controls how this server serves zone transfers out.
/// </summary>
public class ZoneTransferOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneTransfer";

    /// <summary>
    ///     This property contains how many resource records a single AXFR/IXFR response message
    ///     may carry before this server starts a new one. A zone transfer response is not
    ///     chunked by an exact byte budget -- that would mean speculatively re-serializing each
    ///     candidate message just to measure it -- but by this simpler, still effective record
    ///     count, which keeps every message comfortably inside a TCP DNS message's practical size
    ///     limits for any realistic mix of record types.
    /// </summary>
    public int RecordsPerMessage { get; set; } = 100;
}
