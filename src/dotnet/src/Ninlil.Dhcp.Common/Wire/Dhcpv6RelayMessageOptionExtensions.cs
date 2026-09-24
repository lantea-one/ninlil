using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class provides strongly-typed accessors for the options a
///     <see cref="Dhcpv6RelayMessage" /> actually carries -- the Relay Message option (RFC 8415
///     Section 21.10, mandatory), the Interface-ID option (RFC 8415 Section 21.18, optional), and
///     the Remote-ID option (RFC 4649, optional) -- the relay-message sibling of
///     <see cref="Dhcpv6MessageOptionExtensions" />. Every option here is treated as an opaque byte
///     string, the same "this project's own wire/transport layer needs no further interpretation"
///     reasoning <see cref="Dhcpv6OptionCodeEnum.ClientIdentifier" />'s own doc comment already
///     gives for a DUID -- for the Remote-ID option specifically, this means its own 4-octet
///     enterprise-number prefix and variable-length remote-id value are kept together as one raw
///     value rather than split apart, since a tag rule matching it (see
///     <c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>) only ever needs
///     byte-for-byte equality against an operator-configured value, never to inspect either field
///     individually.
/// </summary>
public static class Dhcpv6RelayMessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the Relay Message option's own nested payload -- the raw,
    ///     still-encoded bytes of the message actually being relayed (RFC 8415 Section 21.10).
    /// </summary>
    public static bool TryGetRelayMessagePayload(
        this Dhcpv6RelayMessage message,
        out byte[] payload
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.RelayMessage);

        if (option is null)
        {
            payload = null;
            return false;
        }

        payload = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets (replacing any existing instance) the Relay Message option's own
    ///     nested payload.
    /// </summary>
    public static void SetRelayMessagePayload(this Dhcpv6RelayMessage message, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (payload is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", nameof(payload));

        SetOption(message, Dhcpv6OptionCodeEnum.RelayMessage, payload);
    }

    /// <summary>
    ///     This method attempts to read the Interface-ID option's own opaque bytes (RFC 8415
    ///     Section 21.18).
    /// </summary>
    public static bool TryGetInterfaceId(this Dhcpv6RelayMessage message, out byte[] interfaceId)
    {
        ArgumentNullException.ThrowIfNull(message);
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.InterfaceId);

        if (option is not { Data.Length: > 0 })
        {
            interfaceId = null;
            return false;
        }

        interfaceId = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets (replacing any existing instance) the Interface-ID option's own opaque
    ///     bytes.
    /// </summary>
    public static void SetInterfaceId(this Dhcpv6RelayMessage message, byte[] interfaceId)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (interfaceId is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", nameof(interfaceId));

        SetOption(message, Dhcpv6OptionCodeEnum.InterfaceId, interfaceId);
    }

    /// <summary>
    ///     This method attempts to read the Remote-ID option's own raw value bytes (RFC 4649, code
    ///     37) -- the 4-octet enterprise number and variable-length remote-id value together,
    ///     unparsed. See this class's own doc comment for why the two fields are not split apart.
    /// </summary>
    public static bool TryGetRemoteId(this Dhcpv6RelayMessage message, out byte[] remoteId)
    {
        ArgumentNullException.ThrowIfNull(message);
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.RemoteId);

        // RFC 4649's own wire format always carries at least the 4-octet enterprise number, even
        // when the relay agent's own remote-id value is empty -- fewer than 4 bytes is malformed.
        if (option is not { Data.Length: >= 4 })
        {
            remoteId = null;
            return false;
        }

        remoteId = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets (replacing any existing instance) the Remote-ID option's own raw value
    ///     bytes (RFC 4649, code 37) -- used only by this project's own test suite to construct a
    ///     realistic relayed request, since this project never itself originates a RELAY-FORW
    ///     carrying one (only a real external relay agent would).
    /// </summary>
    public static void SetRemoteId(this Dhcpv6RelayMessage message, byte[] remoteId)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (remoteId is not { Length: >= 4 })
            throw new ArgumentException(
                "Must be at least 4 bytes (the enterprise number alone).",
                nameof(remoteId)
            );

        SetOption(message, Dhcpv6OptionCodeEnum.RemoteId, remoteId);
    }

    /// <summary>
    ///     This method finds the first option carrying a given code, or <see langword="null" /> if
    ///     this message carries none.
    /// </summary>
    private static Dhcpv6Option FindOption(Dhcpv6RelayMessage message, Dhcpv6OptionCodeEnum code)
    {
        ushort rawCode = (ushort)code;

        foreach (Dhcpv6Option option in message.Options)
            if (option.Code == rawCode)
                return option;

        return null;
    }

    /// <summary>
    ///     This method replaces (or adds, if absent) the option carrying a given code.
    /// </summary>
    private static void SetOption(
        Dhcpv6RelayMessage message,
        Dhcpv6OptionCodeEnum code,
        byte[] data
    )
    {
        ushort rawCode = (ushort)code;
        message.Options.RemoveAll(option => option.Code == rawCode);
        message.Options.Add(new Dhcpv6Option(rawCode, data));
    }
}
