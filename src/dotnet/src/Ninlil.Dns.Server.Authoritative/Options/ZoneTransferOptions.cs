// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Options;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:ZoneTransfer</c> section of the
///     application's configuration and controls how <see cref="Transfer.ZoneTransferService" />
///     serves outgoing zone transfers.
/// </summary>
public class ZoneTransferOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:ZoneTransfer";

    /// <summary>
    ///     This property contains how many records one AXFR/IXFR response message carries before
    ///     a new message starts. Messages are split by record count rather than bytes, which would
    ///     need each message serialized to measure it. 100 keeps typical records well under the
    ///     65,535-byte TCP message limit, though a set of very large records could exceed it.
    /// </summary>
    public int RecordsPerMessage { get; set; } = 100;
}
