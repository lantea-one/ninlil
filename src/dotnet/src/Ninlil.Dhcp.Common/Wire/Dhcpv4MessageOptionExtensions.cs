using System.Buffers.Binary;
using System.Net;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class provides typed accessors for the DHCPv4 options the server and relay agent
///     work with, on top of the raw <see cref="Dhcpv4Message.Options" /> list, plus
///     <see cref="TryGetRawOption" /> and <see cref="SetRawOption" /> for any other tag.
///     <para>
///         Most DHCP options are one of a few shapes (one IPv4 address, a list of them, a 32-bit
///         integer, an ASCII string), so there is no per-type codec registry like
///         <c>Ninlil.Dns.Common.Wire.Records.DnsRecordDataCodecRegistry</c>. Each option gets a
///         <c>TryGetX</c>/<c>SetX</c> pair built on a few private encode and decode helpers. A
///         <c>TryGetX</c> method returns <see langword="false" /> when the option is missing or
///         malformed; a <c>SetX</c> method replaces any existing instance of the option.
///     </para>
/// </summary>
public static class Dhcpv4MessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the DHCP Message Type option (tag 53).
    /// </summary>
    public static bool TryGetMessageType(
        this Dhcpv4Message message,
        out Dhcpv4MessageTypeEnum messageType
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.MessageType);

        if (option is not { Data.Length: 1 })
        {
            messageType = default;
            return false;
        }

        messageType = (Dhcpv4MessageTypeEnum)option.Data[0];
        return true;
    }

    /// <summary>
    ///     This method sets the DHCP Message Type option (tag 53).
    /// </summary>
    public static void SetMessageType(
        this Dhcpv4Message message,
        Dhcpv4MessageTypeEnum messageType
    ) => SetOption(message, Dhcpv4OptionTagEnum.MessageType, [(byte)messageType]);

    /// <summary>
    ///     This method attempts to read the Server Identifier option (tag 54).
    /// </summary>
    public static bool TryGetServerIdentifier(
        this Dhcpv4Message message,
        out IPAddress serverIdentifier
    ) =>
        TryDecodeIpAddress(
            FindOption(message, Dhcpv4OptionTagEnum.ServerIdentifier),
            out serverIdentifier
        );

    /// <summary>
    ///     This method sets the Server Identifier option (tag 54).
    /// </summary>
    public static void SetServerIdentifier(
        this Dhcpv4Message message,
        IPAddress serverIdentifier
    ) =>
        SetOption(message, Dhcpv4OptionTagEnum.ServerIdentifier, EncodeIpAddress(serverIdentifier));

    /// <summary>
    ///     This method attempts to read the Requested IP Address option (tag 50).
    /// </summary>
    public static bool TryGetRequestedIpAddress(
        this Dhcpv4Message message,
        out IPAddress requestedIpAddress
    ) =>
        TryDecodeIpAddress(
            FindOption(message, Dhcpv4OptionTagEnum.RequestedIpAddress),
            out requestedIpAddress
        );

    /// <summary>
    ///     This method sets the Requested IP Address option (tag 50).
    /// </summary>
    public static void SetRequestedIpAddress(
        this Dhcpv4Message message,
        IPAddress requestedIpAddress
    ) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.RequestedIpAddress,
            EncodeIpAddress(requestedIpAddress)
        );

    /// <summary>
    ///     This method attempts to read the Subnet Mask option (tag 1).
    /// </summary>
    public static bool TryGetSubnetMask(this Dhcpv4Message message, out IPAddress subnetMask) =>
        TryDecodeIpAddress(FindOption(message, Dhcpv4OptionTagEnum.SubnetMask), out subnetMask);

    /// <summary>
    ///     This method sets the Subnet Mask option (tag 1).
    /// </summary>
    public static void SetSubnetMask(this Dhcpv4Message message, IPAddress subnetMask) =>
        SetOption(message, Dhcpv4OptionTagEnum.SubnetMask, EncodeIpAddress(subnetMask));

    /// <summary>
    ///     This method attempts to read the IP Address Lease Time option (tag 51).
    /// </summary>
    public static bool TryGetIpAddressLeaseTime(this Dhcpv4Message message, out TimeSpan leaseTime)
    {
        if (
            !TryDecodeUInt32(
                FindOption(message, Dhcpv4OptionTagEnum.IpAddressLeaseTime),
                out uint seconds
            )
        )
        {
            leaseTime = default;
            return false;
        }

        leaseTime = TimeSpan.FromSeconds(seconds);
        return true;
    }

    /// <summary>
    ///     This method sets the IP Address Lease Time option (tag 51).
    /// </summary>
    public static void SetIpAddressLeaseTime(this Dhcpv4Message message, TimeSpan leaseTime)
    {
        double totalSeconds = leaseTime.TotalSeconds;

        if (totalSeconds < 0 || totalSeconds > uint.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(leaseTime),
                leaseTime,
                "A DHCPv4 lease time must fit in an unsigned 32-bit number of seconds."
            );

        SetOption(
            message,
            Dhcpv4OptionTagEnum.IpAddressLeaseTime,
            EncodeUInt32((uint)totalSeconds)
        );
    }

    /// <summary>
    ///     This method attempts to read the Router option (tag 3): one or more router addresses,
    ///     in order of preference.
    /// </summary>
    public static bool TryGetRouters(
        this Dhcpv4Message message,
        out IReadOnlyList<IPAddress> routers
    ) => TryDecodeIpAddressList(FindOption(message, Dhcpv4OptionTagEnum.Router), out routers);

    /// <summary>
    ///     This method sets the Router option (tag 3).
    /// </summary>
    public static void SetRouters(this Dhcpv4Message message, IReadOnlyList<IPAddress> routers) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.Router,
            EncodeIpAddressList(routers, nameof(routers))
        );

    /// <summary>
    ///     This method attempts to read the Domain Name Server option (tag 6): one or more DNS
    ///     server addresses, in order of preference.
    /// </summary>
    public static bool TryGetDomainNameServers(
        this Dhcpv4Message message,
        out IReadOnlyList<IPAddress> domainNameServers
    ) =>
        TryDecodeIpAddressList(
            FindOption(message, Dhcpv4OptionTagEnum.DomainNameServer),
            out domainNameServers
        );

    /// <summary>
    ///     This method sets the Domain Name Server option (tag 6).
    /// </summary>
    public static void SetDomainNameServers(
        this Dhcpv4Message message,
        IReadOnlyList<IPAddress> domainNameServers
    ) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.DomainNameServer,
            EncodeIpAddressList(domainNameServers, nameof(domainNameServers))
        );

    /// <summary>
    ///     This method attempts to read the Host Name option (tag 12).
    /// </summary>
    public static bool TryGetHostName(this Dhcpv4Message message, out string hostName)
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.HostName);

        if (option is null)
        {
            hostName = null;
            return false;
        }

        hostName = Encoding.ASCII.GetString(option.Data);
        return true;
    }

    /// <summary>
    ///     This method sets the Host Name option (tag 12).
    /// </summary>
    public static void SetHostName(this Dhcpv4Message message, string hostName) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.HostName,
            Encoding.ASCII.GetBytes(hostName ?? string.Empty)
        );

    /// <summary>
    ///     This method attempts to read the TFTP Server Name option (tag 66) as ASCII text (see
    ///     <see cref="Dhcpv4OptionTagEnum.TftpServerName" />).
    /// </summary>
    public static bool TryGetTftpServerNameOption(
        this Dhcpv4Message message,
        out string tftpServerName
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.TftpServerName);

        if (option is null)
        {
            tftpServerName = null;
            return false;
        }

        tftpServerName = Encoding.ASCII.GetString(option.Data);
        return true;
    }

    /// <summary>
    ///     This method sets the TFTP Server Name option (tag 66).
    /// </summary>
    public static void SetTftpServerNameOption(this Dhcpv4Message message, string tftpServerName) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.TftpServerName,
            Encoding.ASCII.GetBytes(tftpServerName ?? string.Empty)
        );

    /// <summary>
    ///     This method attempts to read the Bootfile Name option (tag 67) as ASCII text (see
    ///     <see cref="Dhcpv4OptionTagEnum.BootfileName" />).
    /// </summary>
    public static bool TryGetBootfileNameOption(this Dhcpv4Message message, out string bootfileName)
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.BootfileName);

        if (option is null)
        {
            bootfileName = null;
            return false;
        }

        bootfileName = Encoding.ASCII.GetString(option.Data);
        return true;
    }

    /// <summary>
    ///     This method sets the Bootfile Name option (tag 67).
    /// </summary>
    public static void SetBootfileNameOption(this Dhcpv4Message message, string bootfileName) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.BootfileName,
            Encoding.ASCII.GetBytes(bootfileName ?? string.Empty)
        );

    /// <summary>
    ///     This method attempts to read the Parameter Request List option (tag 55): the option
    ///     tags a client wants in the reply.
    /// </summary>
    public static bool TryGetParameterRequestList(
        this Dhcpv4Message message,
        out IReadOnlyList<byte> requestedTags
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.ParameterRequestList);

        if (option is null)
        {
            requestedTags = null;
            return false;
        }

        requestedTags = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets the Parameter Request List option (tag 55).
    /// </summary>
    public static void SetParameterRequestList(
        this Dhcpv4Message message,
        IReadOnlyList<byte> requestedTags
    )
    {
        ArgumentNullException.ThrowIfNull(requestedTags);
        SetOption(message, Dhcpv4OptionTagEnum.ParameterRequestList, [.. requestedTags]);
    }

    /// <summary>
    ///     This method attempts to read the Client-identifier option (tag 61). Per RFC 2132
    ///     Section 9.14, the value is a type octet followed by at least one identifier byte
    ///     (usually the hardware address again). Values shorter than 2 bytes count as absent;
    ///     the bytes are not interpreted further here.
    /// </summary>
    public static bool TryGetClientIdentifier(
        this Dhcpv4Message message,
        out byte[] clientIdentifier
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.ClientIdentifier);

        if (option is not { Data.Length: >= 2 })
        {
            clientIdentifier = null;
            return false;
        }

        clientIdentifier = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets the Client-identifier option (tag 61). The value must be at least 2
    ///     bytes.
    /// </summary>
    public static void SetClientIdentifier(this Dhcpv4Message message, byte[] clientIdentifier)
    {
        if (clientIdentifier is not { Length: >= 2 })
            throw new ArgumentException(
                "A client identifier must be at least 2 bytes: a type octet plus at least one identifier byte.",
                nameof(clientIdentifier)
            );

        SetOption(message, Dhcpv4OptionTagEnum.ClientIdentifier, clientIdentifier);
    }

    /// <summary>
    ///     This method attempts to read the Vendor Class Identifier option (tag 60, RFC 2132
    ///     Section 9.13), an opaque string naming the client's vendor or hardware class. It is
    ///     read as ASCII text, like <see cref="TryGetHostName" />; RFC 2132 defines no further
    ///     structure.
    /// </summary>
    public static bool TryGetVendorClassIdentifier(
        this Dhcpv4Message message,
        out string vendorClassIdentifier
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.VendorClassIdentifier);

        if (option is null)
        {
            vendorClassIdentifier = null;
            return false;
        }

        vendorClassIdentifier = Encoding.ASCII.GetString(option.Data);
        return true;
    }

    /// <summary>
    ///     This method sets the Vendor Class Identifier option (tag 60).
    /// </summary>
    public static void SetVendorClassIdentifier(
        this Dhcpv4Message message,
        string vendorClassIdentifier
    ) =>
        SetOption(
            message,
            Dhcpv4OptionTagEnum.VendorClassIdentifier,
            Encoding.ASCII.GetBytes(vendorClassIdentifier ?? string.Empty)
        );

    /// <summary>
    ///     This method attempts to read the User Class option (tag 77, RFC 3004 Section 3): one or
    ///     more "User Class Data" instances back to back, each a one-byte length followed by that
    ///     many bytes, read as ASCII. Unlike the RFC 3046 sub-options below, there is no type
    ///     octet.
    /// </summary>
    public static bool TryGetUserClasses(
        this Dhcpv4Message message,
        out IReadOnlyList<string> userClasses
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.UserClass);

        if (option is not { Data.Length: > 0 })
        {
            userClasses = null;
            return false;
        }

        List<string> result = [];
        int offset = 0;

        while (offset < option.Data.Length)
        {
            int length = option.Data[offset];
            offset++;

            // On a zero length or one that runs past the end, stop and keep the instances
            // already decoded rather than discarding the whole option.
            if (length == 0 || offset + length > option.Data.Length)
                break;

            result.Add(Encoding.ASCII.GetString(option.Data, offset, length));
            offset += length;
        }

        if (result.Count == 0)
        {
            userClasses = null;
            return false;
        }

        userClasses = result;
        return true;
    }

    /// <summary>
    ///     This method sets the User Class option (tag 77) from one or more User Class Data
    ///     instances, per RFC 3004 Section 3.
    /// </summary>
    public static void SetUserClasses(this Dhcpv4Message message, IReadOnlyList<string> userClasses)
    {
        if (userClasses is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one user class instance.",
                nameof(userClasses)
            );

        using MemoryStream buffer = new();

        foreach (string userClass in userClasses)
        {
            byte[] instanceBytes = Encoding.ASCII.GetBytes(userClass ?? string.Empty);

            if (instanceBytes.Length is 0 or > 255)
                throw new ArgumentException(
                    "Each user class instance must be between 1 and 255 bytes long.",
                    nameof(userClasses)
                );

            buffer.WriteByte((byte)instanceBytes.Length);
            buffer.Write(instanceBytes);
        }

        SetOption(message, Dhcpv4OptionTagEnum.UserClass, buffer.ToArray());
    }

    /// <summary>
    ///     This constant contains RFC 3046 Section 2.1's Agent Circuit ID sub-option tag, nested
    ///     inside the Relay Agent Information option.
    /// </summary>
    private const byte RelayAgentCircuitIdSubOption = 1;

    /// <summary>
    ///     This constant contains RFC 3046 Section 2.2's Agent Remote ID sub-option tag, nested
    ///     inside the Relay Agent Information option.
    /// </summary>
    private const byte RelayAgentRemoteIdSubOption = 2;

    /// <summary>
    ///     This method attempts to read the Agent Circuit ID sub-option (RFC 3046 Section 2.1)
    ///     nested inside the Relay Agent Information option (tag 82). The server engine reads it
    ///     from relayed requests for tag rules (see
    ///     <see cref="Dhcpv4OptionTagEnum.RelayAgentInformation" />).
    /// </summary>
    public static bool TryGetRelayAgentCircuitId(
        this Dhcpv4Message message,
        out byte[] circuitId
    ) => TryFindRelayAgentSubOption(message, RelayAgentCircuitIdSubOption, out circuitId);

    /// <summary>
    ///     This method attempts to read the Agent Remote ID sub-option (RFC 3046 Section 2.2)
    ///     nested inside the Relay Agent Information option (tag 82).
    /// </summary>
    public static bool TryGetRelayAgentRemoteId(this Dhcpv4Message message, out byte[] remoteId) =>
        TryFindRelayAgentSubOption(message, RelayAgentRemoteIdSubOption, out remoteId);

    /// <summary>
    ///     This method sets the Relay Agent Information option (tag 82) from an Agent Circuit ID,
    ///     an Agent Remote ID, or both; at least one must be non-empty. The relay agent
    ///     (<c>Ninlil.Dhcp.Server.Relay</c>) uses it to add its configured IDs to forwarded
    ///     requests, and tests use it to build relayed requests. Each value must fit a one-byte
    ///     length; a longer value is not rejected and produces a malformed sub-option.
    /// </summary>
    public static void SetRelayAgentInformation(
        this Dhcpv4Message message,
        byte[] circuitId = null,
        byte[] remoteId = null
    )
    {
        using MemoryStream buffer = new();

        if (circuitId is { Length: > 0 })
        {
            buffer.WriteByte(RelayAgentCircuitIdSubOption);
            buffer.WriteByte((byte)circuitId.Length);
            buffer.Write(circuitId);
        }

        if (remoteId is { Length: > 0 })
        {
            buffer.WriteByte(RelayAgentRemoteIdSubOption);
            buffer.WriteByte((byte)remoteId.Length);
            buffer.Write(remoteId);
        }

        if (buffer.Length == 0)
            throw new ArgumentException(
                "Must supply at least one of a circuit ID or a remote ID.",
                nameof(circuitId)
            );

        SetOption(message, Dhcpv4OptionTagEnum.RelayAgentInformation, buffer.ToArray());
    }

    /// <summary>
    ///     This method walks the Relay Agent Information option's sub-options and returns the
    ///     value of the first one with the given tag.
    /// </summary>
    private static bool TryFindRelayAgentSubOption(
        Dhcpv4Message message,
        byte subOptionTag,
        out byte[] value
    )
    {
        Dhcpv4Option option = FindOption(message, Dhcpv4OptionTagEnum.RelayAgentInformation);

        if (option is not { Data.Length: > 0 })
        {
            value = null;
            return false;
        }

        int offset = 0;

        while (offset + 2 <= option.Data.Length)
        {
            byte tag = option.Data[offset];
            byte length = option.Data[offset + 1];
            offset += 2;

            // A sub-option whose length runs past the end leaves nothing after it to trust.
            if (offset + length > option.Data.Length)
                break;

            if (tag == subOptionTag)
            {
                value = option.Data.AsSpan(offset, length).ToArray();
                return true;
            }

            offset += length;
        }

        value = null;
        return false;
    }

    /// <summary>
    ///     This method attempts to read any option's raw value by tag, with no decoding. It is the
    ///     reading counterpart of <see cref="SetRawOption" />.
    /// </summary>
    public static bool TryGetRawOption(this Dhcpv4Message message, byte tag, out byte[] data)
    {
        ArgumentNullException.ThrowIfNull(message);

        foreach (Dhcpv4Option option in message.Options)
        {
            if (option.Tag != tag)
                continue;

            data = option.Data;
            return true;
        }

        data = null;
        return false;
    }

    /// <summary>
    ///     This method sets any option's raw value by tag, replacing an existing instance. Custom
    ///     options and tag-rule options (<c>Dhcpv4OptionOverlayService</c>) use it to pass
    ///     configured tag/value pairs through unchanged. A <see langword="null" /> value is stored
    ///     as empty.
    /// </summary>
    public static void SetRawOption(this Dhcpv4Message message, byte tag, byte[] data) =>
        SetOption(message, (Dhcpv4OptionTagEnum)tag, data ?? []);

    /// <summary>
    ///     This method finds the first (in a well-formed message, the only) option with a given
    ///     tag, or <see langword="null" /> if there is none.
    /// </summary>
    private static Dhcpv4Option FindOption(Dhcpv4Message message, Dhcpv4OptionTagEnum tag)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte rawTag = (byte)tag;

        foreach (Dhcpv4Option option in message.Options)
            if (option.Tag == rawTag)
                return option;

        return null;
    }

    /// <summary>
    ///     This method replaces (or adds) the option with a given tag. A message should carry one
    ///     instance of an option, so existing ones are removed first.
    /// </summary>
    private static void SetOption(Dhcpv4Message message, Dhcpv4OptionTagEnum tag, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(message);
        byte rawTag = (byte)tag;
        message.Options.RemoveAll(option => option.Tag == rawTag);
        message.Options.Add(new Dhcpv4Option(rawTag, data));
    }

    /// <summary>
    ///     This method attempts to decode a single-IPv4-address option value.
    /// </summary>
    private static bool TryDecodeIpAddress(Dhcpv4Option option, out IPAddress address)
    {
        if (option is not { Data.Length: 4 })
        {
            address = null;
            return false;
        }

        address = new IPAddress(option.Data);
        return true;
    }

    /// <summary>
    ///     This method encodes a single IPv4 address into an option value.
    /// </summary>
    private static byte[] EncodeIpAddress(IPAddress address)
    {
        if (
            address is null
            || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork
        )
            throw new ArgumentException("Must be an IPv4 address.", nameof(address));

        return address.GetAddressBytes();
    }

    /// <summary>
    ///     This method attempts to decode a value of one or more IPv4 addresses back to back, the
    ///     shape RFC 2132 gives the Router and Domain Name Server options among others.
    /// </summary>
    private static bool TryDecodeIpAddressList(
        Dhcpv4Option option,
        out IReadOnlyList<IPAddress> addresses
    )
    {
        if (option is null || option.Data.Length == 0 || option.Data.Length % 4 != 0)
        {
            addresses = null;
            return false;
        }

        List<IPAddress> list = new(option.Data.Length / 4);

        for (int offset = 0; offset < option.Data.Length; offset += 4)
            list.Add(new IPAddress(option.Data.AsSpan(offset, 4).ToArray()));

        addresses = list;
        return true;
    }

    /// <summary>
    ///     This method encodes one or more IPv4 addresses back to back into an option value.
    /// </summary>
    private static byte[] EncodeIpAddressList(
        IReadOnlyList<IPAddress> addresses,
        string parameterName
    )
    {
        if (addresses is null || addresses.Count == 0)
            throw new ArgumentException("Must contain at least one address.", parameterName);

        byte[] result = new byte[addresses.Count * 4];

        for (int i = 0; i < addresses.Count; i++)
            EncodeIpAddress(addresses[i]).CopyTo(result, i * 4);

        return result;
    }

    /// <summary>
    ///     This method attempts to decode a big-endian, 32-bit option value.
    /// </summary>
    private static bool TryDecodeUInt32(Dhcpv4Option option, out uint value)
    {
        if (option is not { Data.Length: 4 })
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32BigEndian(option.Data);
        return true;
    }

    /// <summary>
    ///     This method encodes a big-endian, 32-bit option value.
    /// </summary>
    private static byte[] EncodeUInt32(uint value)
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(data, value);
        return data;
    }
}
