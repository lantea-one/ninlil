// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:Notify</c> section of the
///     application's configuration and controls how <see cref="UdpNotifySender" /> sends RFC 1996
///     NOTIFY messages to a zone's secondaries. It is read on each send.
/// </summary>
public class NotifyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:Notify";

    /// <summary>
    ///     This property contains how many times in total a NOTIFY is sent to one secondary before
    ///     giving up. RFC 1996 only says a primary may retry, without a count.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    ///     This property contains how long to wait for a matching NOTIFY acknowledgement before
    ///     considering a single attempt to have failed.
    /// </summary>
    public TimeSpan ResponseTimeout { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property contains how long to wait between attempts.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>
    ///     This property contains the port secondaries are sent NOTIFY on, for every secondary.
    ///     It is 53 in practice; tests change it to use an unprivileged port.
    /// </summary>
    public int SecondaryPort { get; set; } = 53;
}
