// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Notify;

/// <summary>
///     This class binds to the <c>DnsServer:Authoritative:Notify</c> section of the
///     application's configuration and controls how this server sends RFC 1996 NOTIFY messages
///     to a zone's configured secondaries.
/// </summary>
public class NotifyOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Authoritative:Notify";

    /// <summary>
    ///     This property contains how many times a NOTIFY is sent to a single secondary, in
    ///     total, before giving up on it -- RFC 1996 does not mandate an exact count, only that
    ///     a primary "may retry periodically", so this simply picks a small, fixed number of
    ///     attempts.
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
    ///     This property contains the port every secondary is assumed to listen on. Always 53 in
    ///     practice; configurable purely so a test can point this at a loopback listener without
    ///     needing the privileges binding port 53 itself would require.
    /// </summary>
    public int SecondaryPort { get; set; } = 53;
}
