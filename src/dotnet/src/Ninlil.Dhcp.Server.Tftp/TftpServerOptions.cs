// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class binds to the <c>DhcpServer:Tftp</c> section of the application's configuration
///     and controls the read-only RFC 1350 TFTP server (see <see cref="TftpServer" />).
/// </summary>
public sealed class TftpServerOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Tftp";

    /// <summary>
    ///     This property controls whether the TFTP server starts at boot. It is off by default,
    ///     like the other optional listeners, because serving files from disk without
    ///     authentication is something an operator should choose. The server can still be started
    ///     at runtime when this is off.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the UDP port requests arrive on. The default is 69, the
    ///     well-known TFTP port. Each transfer then runs on a separate ephemeral port (RFC 1350
    ///     Section 4).
    /// </summary>
    public int Port { get; set; } = 69;

    /// <summary>
    ///     This property contains the directory files are served from. A leading <c>/</c>
    ///     in a requested name means this directory. A request for a path that resolves outside it
    ///     through <c>..</c> segments gets an Access Violation error. A symbolic link is followed
    ///     only when its target and every step on the way stay inside the directory; the directory
    ///     itself may be a link. Left empty (the default) or pointing at a
    ///     missing directory, <see cref="TftpServer" /> logs a warning and does not bind, even
    ///     when <see cref="Enable" /> is <see langword="true" />.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long the server waits for the Acknowledgment of a Data
    ///     block before resending it.
    /// </summary>
    public TimeSpan RetransmitTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    ///     This property contains how many times one Data block is resent before the transfer is
    ///     abandoned, so an unresponsive client cannot hold a socket and task open forever.
    /// </summary>
    public int MaxRetransmissions { get; set; } = 5;
}
