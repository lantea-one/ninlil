// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class holds the fixed sizes of the RFC 8415 client/server and relay wire formats,
///     shared by <see cref="Dhcpv6MessageReader" />, <see cref="Dhcpv6MessageWriter" />,
///     <see cref="Dhcpv6RelayMessageReader" /> and <see cref="Dhcpv6RelayMessageWriter" /> so they
///     cannot drift apart. Unlike <c>Dhcpv4Constants</c> there is no magic cookie and no minimum
///     message size: the client/server header is just msg-type and a 3-octet transaction ID.
/// </summary>
public static class Dhcpv6Constants
{
    /// <summary>
    ///     The length, in bytes, of a client/server message's fixed header: the one-byte
    ///     "msg-type" and the 3-byte "transaction-id" (RFC 8415 Section 8). Relay messages use
    ///     <see cref="RelayFixedHeaderLength" /> instead.
    /// </summary>
    public const int FixedHeaderLength = 4;

    /// <summary>
    ///     The length, in bytes, of a RELAY-FORW or RELAY-REPL fixed header (RFC 8415 Section
    ///     7.1/7.2): one-byte "msg-type", one-byte "hop-count", 16-byte "link-address" and 16-byte
    ///     "peer-address". The options that follow must carry a Relay Message option (Section
    ///     21.10) holding the relayed message.
    /// </summary>
    public const int RelayFixedHeaderLength = 34;

    /// <summary>
    ///     The largest value the 3-octet "transaction-id" field (RFC 8415 Section 8) can hold.
    ///     <see cref="Dhcpv6MessageWriter" /> rejects a larger value as a caller bug rather than
    ///     silently truncating it.
    /// </summary>
    public const uint MaximumTransactionId = 0xFFFFFF;

    /// <summary>
    ///     The length, in bytes, of an option header: a 2-byte "option-code" and a 2-byte
    ///     "option-len" (RFC 8415 Section 21.1). Every options area uses it, at the top level of a
    ///     message and nested inside IA_NA, IA_TA, IA_PD, IAADDR and IAPREFIX values.
    /// </summary>
    public const int OptionHeaderLength = 4;
}
