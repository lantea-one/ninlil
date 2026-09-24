// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class binds to the <c>DhcpServer:Tftp</c> section of the application's configuration
///     and controls this project's own read-only RFC 1350 TFTP server -- see
///     <see cref="TftpServer" />'s own doc comment for the server itself.
/// </summary>
public sealed class TftpServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Tftp";

    /// <summary>
    ///     This property contains whether this server's own TFTP listener is active at all --
    ///     off by default, this project's own established "capability-expanding toggles default
    ///     off" invariant: a TFTP server serving files from disk over the network is a real,
    ///     new attack surface an operator must deliberately opt into, exactly like every other
    ///     optional transport (DoT/DoQ/DNSCrypt/the DHCPv4 listener itself) already does.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the local UDP port this server listens for incoming Read/Write
    ///     Requests on -- RFC 1350's own well-known TFTP port, 69, by default. Each individual
    ///     file transfer then proceeds over its own separate, OS-assigned ephemeral port, per RFC
    ///     1350 Section 4's own transfer-ID mechanism -- this setting governs only the one port
    ///     new requests arrive on.
    /// </summary>
    public int Port { get; set; } = 69;

    /// <summary>
    ///     This property contains the directory this server serves files from. A Read Request
    ///     naming a path that would resolve outside this directory (a leading <c>..</c> segment,
    ///     an absolute path, or a symlink escaping it) is always rejected with an Access
    ///     Violation error -- see <see cref="TftpServer" />'s own doc comment for the exact check.
    ///     Left empty (the default), this server has nothing to serve and
    ///     <see cref="TftpServer" /> logs a warning and never binds a socket even when
    ///     <see cref="Enable" /> is <see langword="true" />, the same defensive "a real
    ///     precondition is missing, so don't pretend to be running" posture a misconfigured ACME
    ///     domain list already establishes elsewhere in this project.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long this server waits for a client's own Acknowledgment
    ///     of a Data block before retransmitting it -- RFC 1350 Section 3's own "any packet may
    ///     be lost" retransmission model.
    /// </summary>
    public TimeSpan RetransmitTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     This property contains the greatest number of times this server retransmits one Data
    ///     block before giving up on the whole transfer -- a genuinely unresponsive or
    ///     disconnected client must never leave this server's own transfer loop, and the
    ///     ephemeral socket/task backing it, running forever.
    /// </summary>
    public int MaxRetransmissions { get; set; } = 5;
}
