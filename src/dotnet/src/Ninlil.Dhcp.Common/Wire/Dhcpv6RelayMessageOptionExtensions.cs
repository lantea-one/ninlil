using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class provides typed accessors for the options of a <see cref="Dhcpv6RelayMessage" />:
///     Relay Message (RFC 8415 Section 21.10, mandatory), Interface-ID (RFC 8415 Section 21.18,
///     optional) and Remote-ID (RFC 4649, optional). It is the relay counterpart of
///     <see cref="Dhcpv6MessageOptionExtensions" />. All three values are opaque byte strings. The
///     Remote-ID's 4-octet enterprise number and remote-id value are kept together, because tag
///     rules (<c>Ninlil.Dhcp.Server.Core.Options.Dhcpv6OptionOverlayService</c>) only compare the
///     whole value with a configured one.
/// </summary>
public static class Dhcpv6RelayMessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the Relay Message option's payload: the encoded message
    ///     being relayed (RFC 8415 Section 21.10).
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
    ///     This method sets the Relay Message option's payload, replacing any existing one. The
    ///     payload must not be empty.
    /// </summary>
    public static void SetRelayMessagePayload(this Dhcpv6RelayMessage message, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (payload is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", nameof(payload));

        SetOption(message, Dhcpv6OptionCodeEnum.RelayMessage, payload);
    }

    /// <summary>
    ///     This method attempts to read the Interface-ID option (RFC 8415 Section 21.18). An empty
    ///     option counts as absent.
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
    ///     This method sets the Interface-ID option, replacing any existing one. The value must
    ///     not be empty.
    /// </summary>
    public static void SetInterfaceId(this Dhcpv6RelayMessage message, byte[] interfaceId)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (interfaceId is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", nameof(interfaceId));

        SetOption(message, Dhcpv6OptionCodeEnum.InterfaceId, interfaceId);
    }

    /// <summary>
    ///     This method attempts to read the Remote-ID option (RFC 4649, code 37): the 4-octet
    ///     enterprise number and the remote-id value together, unparsed (see the class summary).
    /// </summary>
    public static bool TryGetRemoteId(this Dhcpv6RelayMessage message, out byte[] remoteId)
    {
        ArgumentNullException.ThrowIfNull(message);
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.RemoteId);

        // The option always carries the 4-octet enterprise number, even with an empty remote-id,
        // so fewer than 4 bytes is malformed.
        if (option is not { Data.Length: >= 4 })
        {
            remoteId = null;
            return false;
        }

        remoteId = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets the Remote-ID option (RFC 4649, code 37), replacing any existing one.
    ///     Only tests use it, to build relayed requests: Ninlil's relay agent does not send a
    ///     Remote-ID; external relay agents do.
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
