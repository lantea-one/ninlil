using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverQuic;

/// <summary>
///     This class binds to the <c>DnsServer:Protocol:DnsOverQuic</c> section of the
///     application's configuration and controls how <see cref="DnsOverQuicListener" /> binds
///     and behaves. Most settings, including <see cref="MaxConcurrentConnections" />, are read when
///     the listener starts and apply on restart; the per-connection settings
///     (<see cref="IdleTimeout" />,
///     <see cref="MaxConcurrentBidirectionalStreams" />) are read for each new connection.
/// </summary>
public class DnsOverQuicOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DnsServer:Protocol:DnsOverQuic";

    /// <summary>
    ///     This property controls whether this transport listens at all. It is off by default, as
    ///     <c>Ninlil.Dns.Protocol.DnsOverTls.DnsOverTlsOptions.Enable</c> is: a new listening port
    ///     and a certificate to manage are changes an operator should opt into.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the UDP port this transport binds to. RFC 9250 assigns UDP port
    ///     853 to DNS-over-QUIC, the number DNS-over-TLS uses over TCP.
    /// </summary>
    public int Port { get; set; } = 853;

    /// <summary>
    ///     This property contains the local addresses this transport binds to, comma-separated: for
    ///     example <c>"192.168.1.1, fd00::1"</c>, or <c>"127.0.0.1"</c> to keep DNS-over-QUIC off every
    ///     network interface. <c>0.0.0.0</c> and <c>::</c> mean every IPv4 or every IPv6 address.
    ///     Empty (the default) listens on every address of both families through one dual-stack
    ///     socket. See <c>Ninlil.Dns.Server.Core.Transport.ListenAddressList</c> for why this is one
    ///     comma-separated string per transport and why only IP literals are accepted.
    ///     <para>
    ///         QUIC differs from the socket-based transports in two ways, both because the QUIC
    ///         library (MsQuic) manages the sockets: <c>::</c> also answers IPv4 clients, and the
    ///         UDP port is reserved on every address even when only some are listed, since the
    ///         library binds a wildcard socket and filters by address itself. Listing addresses
    ///         keeps DNS-over-QUIC from answering on the others, but another program cannot use
    ///         the same UDP port on an unlisted address.
    ///     </para>
    /// </summary>
    public string ListenAddresses { get; set; } = string.Empty;

    /// <summary>
    ///     This property contains how long a QUIC connection may be idle before it is closed. It
    ///     is also the deadline for a client to send its query on a new stream and for the
    ///     server to write each response message; a stream that misses either is reset.
    /// </summary>
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    ///     This property contains how many bidirectional streams (one per query in flight, RFC
    ///     9250 §4.2) one QUIC connection may have open at once.
    /// </summary>
    public int MaxConcurrentBidirectionalStreams { get; set; } = 100;

    /// <summary>
    ///     This property contains how many QUIC connections the listener serves at once, across
    ///     all its listen addresses. A connection over the limit is closed at once with
    ///     <c>DOQ_EXCESSIVE_LOAD</c> (RFC 9250 §4.3). The default matches DNS over TLS. Applies
    ///     on restart.
    /// </summary>
    public int MaxConcurrentConnections { get; set; } = 512;

    /// <summary>
    ///     This property contains this transport's server certificate configuration.
    /// </summary>
    public TlsCertificateOptions Certificate { get; set; } = new();
}
