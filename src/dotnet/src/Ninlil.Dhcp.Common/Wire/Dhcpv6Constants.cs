// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the fixed sizes RFC 8415's client/server and relay wire formats are built
///     from, shared between <see cref="Dhcpv6MessageReader" />/<see cref="Dhcpv6MessageWriter" />
///     and <see cref="Dhcpv6RelayMessageReader" />/<see cref="Dhcpv6RelayMessageWriter" />
///     respectively, so none of the four can ever quietly drift out of sync with the others.
///     Unlike <c>Dhcpv4Constants</c>, DHCPv6 has no magic cookie and no BOOTP-era
///     minimum-message-size convention to carry forward -- RFC 8415's own client/server header is
///     simply msg-type followed by a 3-octet transaction ID, with nothing else fixed before the
///     options area begins.
/// </summary>
public static class Dhcpv6Constants
{
    /// <summary>
    ///     The length, in bytes, of a client/server DHCPv6 message's fixed header -- the one-byte
    ///     "msg-type" field plus the 3-byte "transaction-id" field, per RFC 8415 Section 8 --
    ///     before the variable-length options area that follows it. This does not apply to a
    ///     RELAY-FORW/RELAY-REPL message, whose own fixed header has a completely different shape
    ///     -- see <see cref="RelayFixedHeaderLength" />.
    /// </summary>
    public const int FixedHeaderLength = 4;

    /// <summary>
    ///     The length, in bytes, of a RELAY-FORW/RELAY-REPL message's own fixed header, per RFC
    ///     8415 Section 7.1/7.2: a one-byte "msg-type" field, a one-byte "hop-count" field, a
    ///     16-byte "link-address" field, and a 16-byte "peer-address" field -- before the
    ///     variable-length options area that follows it (which must carry a Relay Message option
    ///     nesting the message actually being relayed, per Section 21.10).
    /// </summary>
    public const int RelayFixedHeaderLength = 34;

    /// <summary>
    ///     The largest value RFC 8415 Section 8's own 3-octet "transaction-id" field can hold.
    ///     <see cref="Dhcpv6MessageWriter" /> rejects a larger value as a caller bug rather than
    ///     silently truncating it.
    /// </summary>
    public const uint MaximumTransactionId = 0xFFFFFF;

    /// <summary>
    ///     The length, in bytes, of one option's own fixed tag/length header -- a 2-byte
    ///     "option-code" followed by a 2-byte "option-len", per RFC 8415 Section 21.1 -- before
    ///     that option's own value bytes. This is shared by every options area this codec reads or
    ///     writes, whether at a message's own top level or nested inside an IA_NA/IA_TA/IA_PD/
    ///     IAADDR/IAPREFIX option's own value.
    /// </summary>
    public const int OptionHeaderLength = 4;
}
