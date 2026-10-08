// Define our namespace.
namespace Ninlil.Dhcp.Server.Relay;

/// <summary>
///     This class binds to the <c>DhcpServer:Relay:Dhcpv4</c> section of the application's
///     configuration and controls whether this server acts as a DHCPv4 relay agent (RFC 2131
///     Section 4.1, with RFC 3046 Relay Agent Information), forwarding client requests to
///     upstream DHCPv4 servers instead of answering them from local scopes, and how.
///     <para>
///         <b>Relay or local engine, not both.</b> One listener cannot both answer and forward
///         the same request. <c>Program.cs</c> reads <see cref="Enable" /> before dependency
///         injection is built and registers either <see cref="Dhcpv4RelayAgent" /> or the
///         <c>Ninlil.Dhcp.Server.Core</c> engine as the <c>IDhcpv4RequestHandler</c>, as it does
///         for the message broker and OIDC scheme. Changing it needs a restart.
///     </para>
/// </summary>
public sealed class Dhcpv4RelayOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DhcpServer:Relay:Dhcpv4";

    /// <summary>
    ///     This property controls whether this server is a DHCPv4 relay agent. It is off by
    ///     default, like <see cref="Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions.Enable" />:
    ///     forwarding a network's DHCP traffic elsewhere changes the network. The listener still
    ///     needs <see cref="Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4ProtocolOptions.Enable" /> to start.
    /// </summary>
    public bool Enable { get; set; }

    /// <summary>
    ///     This property contains the IPv4 addresses of the upstream DHCPv4 servers. Every request
    ///     is sent to all of them, as relay agents usually do, and whichever can serve the client
    ///     answers. Entries that do not parse are skipped with a warning. Left empty (the
    ///     default), every request is dropped with a warning. A reply is relayed to the client
    ///     only when its source address is one of these entries, so a server that answers from a
    ///     different address than the one configured (multi-homed, virtual IP) must be listed by
    ///     that address.
    /// </summary>
    public List<string> UpstreamServerAddresses { get; set; } = [];

    /// <summary>
    ///     This property contains the UDP port the upstream servers listen on. The default is
    ///     67, the IANA DHCP/BOOTP server port. Replies come back to <c>giaddr</c> on the
    ///     listener's server port (RFC 2131 Section 4.1), not on this setting.
    /// </summary>
    public int UpstreamServerPort { get; set; } = 67;

    /// <summary>
    ///     This property optionally contains the IPv4 address this relay puts in <c>giaddr</c>
    ///     when it is the first relay to forward a request. Upstream servers use it to choose the
    ///     scope, so it should be the relay's address on the client subnet. Left
    ///     <see langword="null" /> (the default), it is detected once at startup as for
    ///     <c>Ninlil.Dhcp.Server.Core.DhcpServerOptions.ServerIdentifierAddress</c>; the listener
    ///     does not know a packet's arrival interface, and a relay always has at least two
    ///     interfaces, so this should normally be set.
    /// </summary>
    public string RelayAgentAddress { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.1 Agent Circuit ID, as ASCII
    ///     text, which tells the upstream server which relay interface or circuit the client is
    ///     on. With it or <see cref="RelayAgentRemoteId" /> set, the relay puts a Relay Agent
    ///     Information option (tag 82) on every request it forwards, replacing any already there
    ///     (see <see cref="Common.Wire.Dhcpv4MessageOptionExtensions.SetRelayAgentInformation" />);
    ///     with neither set, the option is left as received. The value must be printable ASCII
    ///     and at most 253 bytes, and the two IDs together at most 251 bytes
    ///     (see <see cref="TryValidateRelayAgentIds" />). With an invalid value the relay logs an
    ///     error once and forwards requests without the option.
    /// </summary>
    public string RelayAgentCircuitId { get; set; }

    /// <summary>
    ///     This property optionally contains the RFC 3046 Section 2.2 Agent Remote ID, as ASCII
    ///     text, usually naming the client or its access device more specifically than the Circuit
    ///     ID. Left <see langword="null" /> or empty (the default), no Remote ID sub-option is
    ///     added; see <see cref="RelayAgentCircuitId" /> for how the two combine.
    /// </summary>
    public string RelayAgentRemoteId { get; set; }

    /// <summary>
    ///     This constant is the longest Circuit ID or Remote ID in bytes: a sub-option holds at
    ///     most 255 bytes (RFC 3046 Section 2.0) and a 2-byte header comes first, so a lone ID
    ///     could reach 253. The option holds 255 bytes, so two IDs together are limited by
    ///     <see cref="MaxCombinedRelayAgentIdLength" />.
    /// </summary>
    public const int MaxRelayAgentIdLength = 253;

    /// <summary>
    ///     This constant is the longest sum of the Circuit ID and Remote ID lengths when both are
    ///     set: two 2-byte sub-option headers leave 251 of the option's 255 bytes.
    /// </summary>
    public const int MaxCombinedRelayAgentIdLength = 251;

    /// <summary>
    ///     This method checks a Circuit ID and Remote ID against the limits of the Relay Agent
    ///     Information option: printable ASCII only (0x20 to 0x7E), each at most
    ///     <see cref="MaxRelayAgentIdLength" /> bytes and, when both are set, together at most
    ///     <see cref="MaxCombinedRelayAgentIdLength" />. Null and empty values are valid.
    ///     The settings API and <see cref="Dhcpv4RelayAgent" /> both use it.
    /// </summary>
    /// <param name="circuitId">The Circuit ID text, or null.</param>
    /// <param name="remoteId">The Remote ID text, or null.</param>
    /// <param name="error">The reason when the IDs are invalid, otherwise null.</param>
    /// <returns>True when both IDs can be encoded.</returns>
    public static bool TryValidateRelayAgentIds(string circuitId, string remoteId, out string error)
    {
        circuitId ??= string.Empty;
        remoteId ??= string.Empty;

        if (!IsPrintableAscii(circuitId) || !IsPrintableAscii(remoteId))
        {
            error = "The circuit ID and remote ID must contain printable ASCII characters only.";
            return false;
        }

        if (circuitId.Length > MaxRelayAgentIdLength || remoteId.Length > MaxRelayAgentIdLength)
        {
            error =
                $"The circuit ID and remote ID must each be at most {MaxRelayAgentIdLength} characters.";
            return false;
        }

        if (
            circuitId.Length > 0
            && remoteId.Length > 0
            && circuitId.Length + remoteId.Length > MaxCombinedRelayAgentIdLength
        )
        {
            error =
                $"The circuit ID and remote ID must be at most {MaxCombinedRelayAgentIdLength} characters together.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    ///     This method says whether every character is printable ASCII (0x20 to 0x7E).
    /// </summary>
    private static bool IsPrintableAscii(string text)
    {
        foreach (char character in text)
        {
            if (character < 0x20 || character > 0x7E)
                return false;
        }

        return true;
    }
}
