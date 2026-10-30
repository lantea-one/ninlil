using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class provides typed accessors for the DHCPv6 options the server works with (RFC
///     8415, RFC 3646 and RFC 4704) on top of the raw <see cref="Dhcpv6Message.Options" /> list.
///     It is the DHCPv6 counterpart of <see cref="Dhcpv4MessageOptionExtensions" /> and follows
///     the same pattern: <c>TryGetX</c> returns <see langword="false" /> when an option is missing
///     or malformed, and <c>SetX</c> replaces any existing instance. The IA_NA, IA_TA and IA_PD
///     options are the exception: each is a container type
///     (<see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" /> and its siblings), and a
///     message can carry several, so their accessors return a list and their setters append.
/// </summary>
public static class Dhcpv6MessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the Client Identifier option's DUID (code 1). The DUID is
    ///     opaque (see <see cref="Dhcpv6OptionCodeEnum.ClientIdentifier" />); an empty option
    ///     counts as absent.
    /// </summary>
    public static bool TryGetClientIdentifier(this Dhcpv6Message message, out byte[] duid) =>
        TryGetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ClientIdentifier, out duid);

    /// <summary>
    ///     This method sets the Client Identifier option (code 1).
    /// </summary>
    public static void SetClientIdentifier(this Dhcpv6Message message, byte[] duid) =>
        SetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ClientIdentifier, duid, nameof(duid));

    /// <summary>
    ///     This method attempts to read the Server Identifier option's DUID (code 2).
    /// </summary>
    public static bool TryGetServerIdentifier(this Dhcpv6Message message, out byte[] duid) =>
        TryGetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ServerIdentifier, out duid);

    /// <summary>
    ///     This method sets the Server Identifier option (code 2).
    /// </summary>
    public static void SetServerIdentifier(this Dhcpv6Message message, byte[] duid) =>
        SetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ServerIdentifier, duid, nameof(duid));

    /// <summary>
    ///     This method attempts to read the Option Request Option (code 6): the option codes a
    ///     client wants in the reply. An empty or odd-length value counts as malformed.
    /// </summary>
    public static bool TryGetOptionRequest(
        this Dhcpv6Message message,
        out IReadOnlyList<ushort> requestedOptionCodes
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.OptionRequest);

        if (option is not { Data.Length: > 0 } || option.Data.Length % 2 != 0)
        {
            requestedOptionCodes = null;
            return false;
        }

        List<ushort> codes = new(option.Data.Length / 2);

        for (int offset = 0; offset < option.Data.Length; offset += 2)
            codes.Add(BinaryPrimitives.ReadUInt16BigEndian(option.Data.AsSpan(offset, 2)));

        requestedOptionCodes = codes;
        return true;
    }

    /// <summary>
    ///     This method sets the Option Request Option (code 6).
    /// </summary>
    public static void SetOptionRequest(
        this Dhcpv6Message message,
        IReadOnlyList<ushort> requestedOptionCodes
    )
    {
        if (requestedOptionCodes is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one requested option code.",
                nameof(requestedOptionCodes)
            );

        byte[] data = new byte[requestedOptionCodes.Count * 2];

        for (int i = 0; i < requestedOptionCodes.Count; i++)
            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2, 2), requestedOptionCodes[i]);

        SetOption(message, Dhcpv6OptionCodeEnum.OptionRequest, data);
    }

    /// <summary>
    ///     This method attempts to read the Preference option (code 7).
    /// </summary>
    public static bool TryGetPreference(this Dhcpv6Message message, out byte preference)
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.Preference);

        if (option is not { Data.Length: 1 })
        {
            preference = 0;
            return false;
        }

        preference = option.Data[0];
        return true;
    }

    /// <summary>
    ///     This method sets the Preference option (code 7).
    /// </summary>
    public static void SetPreference(this Dhcpv6Message message, byte preference) =>
        SetOption(message, Dhcpv6OptionCodeEnum.Preference, [preference]);

    /// <summary>
    ///     This method attempts to read the Elapsed Time option (code 8). Per RFC 8415 Section
    ///     21.9 the value is in hundredths of a second, and 0xFFFF (655.35 seconds) means "that or
    ///     longer"; the capped value is returned as-is.
    /// </summary>
    public static bool TryGetElapsedTime(this Dhcpv6Message message, out TimeSpan elapsed)
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.ElapsedTime);

        if (option is not { Data.Length: 2 })
        {
            elapsed = default;
            return false;
        }

        ushort hundredthsOfASecond = BinaryPrimitives.ReadUInt16BigEndian(option.Data);
        elapsed = TimeSpan.FromMilliseconds(hundredthsOfASecond * 10);
        return true;
    }

    /// <summary>
    ///     This method sets the Elapsed Time option (code 8). A value too large for the 16-bit
    ///     field is clamped to 0xFFFF rather than rejected, since RFC 8415 Section 21.9 has a client use
    ///     that value for any elapsed time above the largest the field holds. A negative value
    ///     throws.
    /// </summary>
    public static void SetElapsedTime(this Dhcpv6Message message, TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(elapsed),
                elapsed,
                "A DHCPv6 elapsed time cannot be negative."
            );

        double hundredthsOfASecond = elapsed.TotalMilliseconds / 10;
        ushort wireValue = (ushort)Math.Min(hundredthsOfASecond, ushort.MaxValue);

        byte[] data = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(data, wireValue);
        SetOption(message, Dhcpv6OptionCodeEnum.ElapsedTime, data);
    }

    /// <summary>
    ///     This method attempts to read the top-level Status Code option (code 13). For a Status
    ///     Code nested in an IA_NA, IA_TA or IA_PD, pass that association's options (e.g.
    ///     <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses.Options" />) to
    ///     <see cref="TryGetStatusCode(IReadOnlyList{Dhcpv6Option},out Dhcpv6StatusCodeEnum,out string)" />.
    /// </summary>
    public static bool TryGetStatusCode(
        this Dhcpv6Message message,
        out Dhcpv6StatusCodeEnum statusCode,
        out string statusMessage
    ) =>
        TryGetStatusCode(
            FindOption(message, Dhcpv6OptionCodeEnum.StatusCode),
            out statusCode,
            out statusMessage
        );

    /// <summary>
    ///     This method attempts to read a Status Code option (code 13) from a nested options
    ///     list, such as an IA_NA, IA_TA or IA_PD association's <c>Options</c>.
    /// </summary>
    public static bool TryGetStatusCode(
        this IReadOnlyList<Dhcpv6Option> nestedOptions,
        out Dhcpv6StatusCodeEnum statusCode,
        out string statusMessage
    )
    {
        ArgumentNullException.ThrowIfNull(nestedOptions);

        Dhcpv6Option option = nestedOptions.FirstOrDefault(o =>
            o.Code == (ushort)Dhcpv6OptionCodeEnum.StatusCode
        );

        return TryGetStatusCode(option, out statusCode, out statusMessage);
    }

    /// <summary>
    ///     This method decodes a Status Code option's value for both public
    ///     <c>TryGetStatusCode</c> overloads. The message text is read as UTF-8.
    /// </summary>
    private static bool TryGetStatusCode(
        Dhcpv6Option option,
        out Dhcpv6StatusCodeEnum statusCode,
        out string statusMessage
    )
    {
        if (option is not { Data.Length: >= 2 })
        {
            statusCode = default;
            statusMessage = null;
            return false;
        }

        statusCode = (Dhcpv6StatusCodeEnum)BinaryPrimitives.ReadUInt16BigEndian(option.Data);
        statusMessage =
            option.Data.Length > 2
                ? Encoding.UTF8.GetString(option.Data, 2, option.Data.Length - 2)
                : string.Empty;
        return true;
    }

    /// <summary>
    ///     This method sets the top-level Status Code option (code 13).
    /// </summary>
    public static void SetStatusCode(
        this Dhcpv6Message message,
        Dhcpv6StatusCodeEnum statusCode,
        string statusMessage = null
    ) =>
        SetOption(
            message,
            Dhcpv6OptionCodeEnum.StatusCode,
            EncodeStatusCode(statusCode, statusMessage)
        );

    /// <summary>
    ///     This method encodes a Status Code option's value (the code and a UTF-8 message). It is
    ///     used by <see cref="SetStatusCode(Dhcpv6Message,Dhcpv6StatusCodeEnum,string)" /> and by
    ///     <c>Dhcpv6RequestHandler</c> to build Status Code options nested in an association.
    /// </summary>
    public static byte[] EncodeStatusCode(
        Dhcpv6StatusCodeEnum statusCode,
        string statusMessage = null
    )
    {
        byte[] messageBytes = Encoding.UTF8.GetBytes(statusMessage ?? string.Empty);
        byte[] data = new byte[2 + messageBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(data, (ushort)statusCode);
        messageBytes.CopyTo(data, 2);
        return data;
    }

    /// <summary>
    ///     This method reports whether the Rapid Commit option (code 14) is present; it is an
    ///     empty marker with no value.
    /// </summary>
    public static bool TryGetRapidCommit(this Dhcpv6Message message) =>
        FindOption(message, Dhcpv6OptionCodeEnum.RapidCommit) is not null;

    /// <summary>
    ///     This method sets the Rapid Commit option (code 14).
    /// </summary>
    public static void SetRapidCommit(this Dhcpv6Message message) =>
        SetOption(message, Dhcpv6OptionCodeEnum.RapidCommit, []);

    /// <summary>
    ///     This method attempts to read the User Class option (RFC 8415 Section 21.15, code 15):
    ///     one or more "user-class-data" instances back to back, each a 2-octet length followed by
    ///     that many octets, read as ASCII like the DHCPv4 User Class
    ///     (<see cref="Dhcpv4MessageOptionExtensions.TryGetUserClasses" />).
    /// </summary>
    public static bool TryGetUserClasses(
        this Dhcpv6Message message,
        out IReadOnlyList<string> userClasses
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.UserClass);

        if (option is not { Data.Length: > 0 })
        {
            userClasses = null;
            return false;
        }

        List<string> result = [];
        int offset = 0;

        while (offset < option.Data.Length)
        {
            // Each instance is a 2-octet length (1 octet in DHCPv4) followed by the data.
            if (offset + 2 > option.Data.Length)
                break;

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(option.Data.AsSpan(offset, 2));
            offset += 2;

            // On a zero length or one that runs past the end, stop and keep the instances
            // already decoded, as the DHCPv4 version does.
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
    ///     This method sets the User Class option (RFC 8415 Section 21.15, code 15).
    /// </summary>
    public static void SetUserClasses(this Dhcpv6Message message, IReadOnlyList<string> userClasses)
    {
        if (userClasses is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one user class instance.",
                nameof(userClasses)
            );

        using MemoryStream buffer = new();
        Span<byte> lengthBytes = stackalloc byte[2];

        foreach (string userClass in userClasses)
        {
            byte[] instanceBytes = Encoding.ASCII.GetBytes(userClass ?? string.Empty);

            if (instanceBytes.Length is 0 or > ushort.MaxValue)
                throw new ArgumentException(
                    "Each user class instance must be between 1 and 65535 bytes long.",
                    nameof(userClasses)
                );

            BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, (ushort)instanceBytes.Length);
            buffer.Write(lengthBytes);
            buffer.Write(instanceBytes);
        }

        SetOption(message, Dhcpv6OptionCodeEnum.UserClass, buffer.ToArray());
    }

    /// <summary>
    ///     This method attempts to read the Vendor Class option (RFC 8415 Section 21.16, code 16):
    ///     a 4-octet IANA Enterprise Number followed by "vendor-class-data" instances, each a
    ///     2-octet length and that many octets, read as ASCII.
    /// </summary>
    /// <param name="message">The message to read the option from.</param>
    /// <param name="enterpriseNumber">Receives the vendor's IANA Enterprise Number.</param>
    /// <param name="vendorClassData">Receives the decoded vendor-class-data instances, in wire order; may be empty.</param>
    public static bool TryGetVendorClass(
        this Dhcpv6Message message,
        out uint enterpriseNumber,
        out IReadOnlyList<string> vendorClassData
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.VendorClass);

        if (option is not { Data.Length: >= 4 })
        {
            enterpriseNumber = 0;
            vendorClassData = null;
            return false;
        }

        enterpriseNumber = BinaryPrimitives.ReadUInt32BigEndian(option.Data.AsSpan(0, 4));

        List<string> result = [];
        int offset = 4;

        while (offset < option.Data.Length)
        {
            if (offset + 2 > option.Data.Length)
                break;

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(option.Data.AsSpan(offset, 2));
            offset += 2;

            // Stop on a malformed instance and keep what was decoded, as TryGetUserClasses does.
            if (length == 0 || offset + length > option.Data.Length)
                break;

            result.Add(Encoding.ASCII.GetString(option.Data, offset, length));
            offset += length;
        }

        // An enterprise number with no instances still identifies the vendor, so it is reported
        // as success with an empty list.
        vendorClassData = result;
        return true;
    }

    /// <summary>
    ///     This method sets the Vendor Class option (RFC 8415 Section 21.16, code 16).
    /// </summary>
    /// <param name="message">The message to set the option on.</param>
    /// <param name="enterpriseNumber">The vendor's IANA Enterprise Number.</param>
    /// <param name="vendorClassData">The vendor-class-data instances to encode, in order; may be empty.</param>
    public static void SetVendorClass(
        this Dhcpv6Message message,
        uint enterpriseNumber,
        IReadOnlyList<string> vendorClassData
    )
    {
        using MemoryStream buffer = new();
        Span<byte> enterpriseNumberBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(enterpriseNumberBytes, enterpriseNumber);
        buffer.Write(enterpriseNumberBytes);

        Span<byte> lengthBytes = stackalloc byte[2];

        foreach (string instance in vendorClassData ?? [])
        {
            byte[] instanceBytes = Encoding.ASCII.GetBytes(instance ?? string.Empty);

            if (instanceBytes.Length is 0 or > ushort.MaxValue)
                throw new ArgumentException(
                    "Each vendor class data instance must be between 1 and 65535 bytes long.",
                    nameof(vendorClassData)
                );

            BinaryPrimitives.WriteUInt16BigEndian(lengthBytes, (ushort)instanceBytes.Length);
            buffer.Write(lengthBytes);
            buffer.Write(instanceBytes);
        }

        SetOption(message, Dhcpv6OptionCodeEnum.VendorClass, buffer.ToArray());
    }

    /// <summary>
    ///     This method attempts to read the DNS Recursive Name Server option (RFC 3646 Section 3,
    ///     code 23): one or more recursive DNS server addresses, in order of preference.
    /// </summary>
    public static bool TryGetDnsServers(
        this Dhcpv6Message message,
        out IReadOnlyList<IPAddress> dnsServers
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.DnsRecursiveNameServer);

        if (option is null || option.Data.Length == 0 || option.Data.Length % 16 != 0)
        {
            dnsServers = null;
            return false;
        }

        List<IPAddress> servers = new(option.Data.Length / 16);

        for (int offset = 0; offset < option.Data.Length; offset += 16)
            servers.Add(new IPAddress(option.Data.AsSpan(offset, 16).ToArray()));

        dnsServers = servers;
        return true;
    }

    /// <summary>
    ///     This method sets the DNS Recursive Name Server option (RFC 3646 Section 3, code 23).
    /// </summary>
    public static void SetDnsServers(
        this Dhcpv6Message message,
        IReadOnlyList<IPAddress> dnsServers
    )
    {
        if (dnsServers is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one DNS server address.",
                nameof(dnsServers)
            );

        byte[] data = new byte[dnsServers.Count * 16];

        for (int i = 0; i < dnsServers.Count; i++)
        {
            if (dnsServers[i] is not { AddressFamily: AddressFamily.InterNetworkV6 } address)
                throw new ArgumentException(
                    "Every DNS server address must be an IPv6 address.",
                    nameof(dnsServers)
                );

            address.TryWriteBytes(data.AsSpan(i * 16, 16), out _);
        }

        SetOption(message, Dhcpv6OptionCodeEnum.DnsRecursiveNameServer, data);
    }

    /// <summary>
    ///     This method attempts to read the Domain Search List option (RFC 3646, code 24): one or
    ///     more domain names for a client to search when resolving an unqualified host name. Only
    ///     uncompressed RFC 1035 label sequences are decoded. A name with an RFC 1035 Section 4.1.4
    ///     compression pointer is rejected, because resolving the pointer would need the whole
    ///     message and this method sees only the option.
    /// </summary>
    public static bool TryGetDomainSearchList(
        this Dhcpv6Message message,
        out IReadOnlyList<string> domains
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.DomainSearchList);

        if (option is not { Data.Length: > 0 })
        {
            domains = null;
            return false;
        }

        return DhcpDomainNameListCodec.TryDecode(option.Data, out domains, out _);
    }

    /// <summary>
    ///     This method sets the Domain Search List option (RFC 3646, code 24), writing
    ///     uncompressed RFC 1035 label sequences.
    /// </summary>
    public static void SetDomainSearchList(
        this Dhcpv6Message message,
        IReadOnlyList<string> domains
    )
    {
        if (domains is not { Count: > 0 })
            throw new ArgumentException("Must contain at least one domain name.", nameof(domains));

        if (!DhcpDomainNameListCodec.TryEncode(domains, out byte[] value, out string error))
            throw new ArgumentException(error, nameof(domains));

        SetOption(message, Dhcpv6OptionCodeEnum.DomainSearchList, value);
    }

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "S" bit, the lowest-order bit of the
    ///     Client FQDN flags octet (<c>0 1 2 3 4 5 6 7 | MBZ |N|O|S|</c>, bit 0 high-order). From
    ///     Section 4.1: "The 'S' bit indicates whether the server
    ///     SHOULD or SHOULD NOT perform the AAAA RR (FQDN-to-address) DNS updates ... The state of
    ///     the bit in the reply from the server indicates the action to be taken by the server; if
    ///     it is 1, the server has taken responsibility for AAAA RR updates for the FQDN."
    /// </summary>
    private const byte ClientFqdnServerPerformsAaaaUpdateBit = 0x01;

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "O" bit, the middle of the three flag
    ///     bits. From Section 4.1: "The 'O' bit indicates whether the server has
    ///     overridden the client's preference for the 'S' bit. A client MUST set this bit to 0. A
    ///     server MUST set this bit to 1 if the 'S' bit in its reply to the client does not match
    ///     the 'S' bit received from the client."
    /// </summary>
    private const byte ClientFqdnServerOverrodeClientPreferenceBit = 0x02;

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "N" bit, the highest of the three flag
    ///     bits. From Section 4.1: "The 'N' bit indicates whether the
    ///     server SHOULD NOT perform any DNS updates ... A server sets the 'N' bit to indicate
    ///     whether the server SHALL (0) or SHALL NOT (1) perform DNS updates. If the 'N' bit is 1,
    ///     the 'S' bit MUST be 0."
    /// </summary>
    private const byte ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit = 0x04;

    /// <summary>
    ///     This method attempts to read the Client FQDN option (RFC 4704, code 39): a fully
    ///     qualified or partial domain name plus the N, O and S flags (see
    ///     <see cref="ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit" />,
    ///     <see cref="ClientFqdnServerOverrodeClientPreferenceBit" /> and
    ///     <see cref="ClientFqdnServerPerformsAaaaUpdateBit" /> for their meaning). The format is
    ///     the same in requests and replies; only who sets the flags differs.
    /// </summary>
    /// <param name="message">The message to read the option from.</param>
    /// <param name="serverShouldNotPerformAnyDnsUpdates">Receives the "N" bit.</param>
    /// <param name="serverOverrodeClientPreference">Receives the "O" bit.</param>
    /// <param name="serverPerformsAaaaUpdate">Receives the "S" bit.</param>
    /// <param name="domainName">
    ///     Receives the decoded domain name (dot-joined labels), or <see langword="null" /> when
    ///     the Domain Name field is empty, which in a request means the client wants the server
    ///     to provide a name (RFC 4704 Section 4.2).
    /// </param>
    /// <param name="isFullyQualified">
    ///     Receives whether the Domain Name field ends with a zero-length root label (a fully
    ///     qualified name in RFC 4704 Section 4.2 terms) rather than without one (a partial name).
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the option is present and its Domain Name field decodes;
    ///     <see langword="false" /> if the option is absent or empty, or the name is malformed (a
    ///     truncated label, trailing bytes, or a compression pointer, which is not supported here
    ///     either; see <see cref="TryGetDomainSearchList" />).
    /// </returns>
    public static bool TryGetClientFqdn(
        this Dhcpv6Message message,
        out bool serverShouldNotPerformAnyDnsUpdates,
        out bool serverOverrodeClientPreference,
        out bool serverPerformsAaaaUpdate,
        out string domainName,
        out bool isFullyQualified
    )
    {
        Dhcpv6Option option = FindOption(message, Dhcpv6OptionCodeEnum.ClientFqdn);

        if (option is not { Data.Length: >= 1 })
        {
            serverShouldNotPerformAnyDnsUpdates = false;
            serverOverrodeClientPreference = false;
            serverPerformsAaaaUpdate = false;
            domainName = null;
            isFullyQualified = false;
            return false;
        }

        byte flags = option.Data[0];
        serverShouldNotPerformAnyDnsUpdates =
            (flags & ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit) != 0;
        serverOverrodeClientPreference = (flags & ClientFqdnServerOverrodeClientPreferenceBit) != 0;
        serverPerformsAaaaUpdate = (flags & ClientFqdnServerPerformsAaaaUpdateBit) != 0;

        byte[] domainNameData = option.Data.Length > 1 ? option.Data[1..] : [];

        return TryDecodeClientFqdnDomainName(domainNameData, out domainName, out isFullyQualified);
    }

    /// <summary>
    ///     This method sets the Client FQDN option (RFC 4704, code 39). The server uses it for
    ///     its reply; tests also use it to build requests. See <see cref="TryGetClientFqdn" /> for
    ///     the format.
    /// </summary>
    /// <param name="message">The message to set the option on.</param>
    /// <param name="serverShouldNotPerformAnyDnsUpdates">The "N" bit to encode.</param>
    /// <param name="serverOverrodeClientPreference">The "O" bit to encode.</param>
    /// <param name="serverPerformsAaaaUpdate">The "S" bit to encode.</param>
    /// <param name="domainName">
    ///     The domain name to encode (dot-joined labels), or <see langword="null" /> or empty to
    ///     write an empty Domain Name field.
    /// </param>
    /// <param name="isFullyQualified">
    ///     Whether to end the name with a zero-length root label (fully qualified) or not
    ///     (partial). Ignored when <paramref name="domainName" /> is <see langword="null" /> or
    ///     empty.
    /// </param>
    public static void SetClientFqdn(
        this Dhcpv6Message message,
        bool serverShouldNotPerformAnyDnsUpdates,
        bool serverOverrodeClientPreference,
        bool serverPerformsAaaaUpdate,
        string domainName,
        bool isFullyQualified
    )
    {
        byte flags = 0;

        if (serverShouldNotPerformAnyDnsUpdates)
            flags |= ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit;
        if (serverOverrodeClientPreference)
            flags |= ClientFqdnServerOverrodeClientPreferenceBit;
        if (serverPerformsAaaaUpdate)
            flags |= ClientFqdnServerPerformsAaaaUpdateBit;

        byte[] domainNameBytes = EncodeClientFqdnDomainName(domainName, isFullyQualified);
        byte[] data = new byte[1 + domainNameBytes.Length];
        data[0] = flags;
        domainNameBytes.CopyTo(data, 1);

        SetOption(message, Dhcpv6OptionCodeEnum.ClientFqdn, data);
    }

    /// <summary>
    ///     This method attempts to read every Identity Association for Non-temporary Addresses
    ///     (IA_NA, code 3) option in the message. A malformed IA_NA is skipped; the method returns
    ///     <see langword="false" /> when none decodes.
    /// </summary>
    public static bool TryGetIdentityAssociationsForNonTemporaryAddresses(
        this Dhcpv6Message message,
        out IReadOnlyList<Dhcpv6IdentityAssociationForNonTemporaryAddresses> associations
    )
    {
        List<Dhcpv6IdentityAssociationForNonTemporaryAddresses> decoded = [];

        foreach (Dhcpv6Option option in FindOptions(message, Dhcpv6OptionCodeEnum.IaNa))
        {
            if (
                !Dhcpv6IdentityAssociationForNonTemporaryAddresses.TryDecode(
                    option.Data,
                    out Dhcpv6IdentityAssociationForNonTemporaryAddresses association,
                    out _
                )
            )
                continue;

            decoded.Add(association);
        }

        if (decoded.Count == 0)
        {
            associations = null;
            return false;
        }

        associations = decoded;
        return true;
    }

    /// <summary>
    ///     This method appends an Identity Association for Non-temporary Addresses (IA_NA, code 3)
    ///     option. It appends rather than replaces, since a message can carry several.
    /// </summary>
    public static void AddIdentityAssociationForNonTemporaryAddresses(
        this Dhcpv6Message message,
        Dhcpv6IdentityAssociationForNonTemporaryAddresses association
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(association);
        message.Options.Add(
            new Dhcpv6Option((ushort)Dhcpv6OptionCodeEnum.IaNa, association.Encode())
        );
    }

    /// <summary>
    ///     This method attempts to read every Identity Association for Temporary Addresses (IA_TA,
    ///     code 4) option in the message. A malformed IA_TA is skipped; the method returns
    ///     <see langword="false" /> when none decodes.
    /// </summary>
    public static bool TryGetIdentityAssociationsForTemporaryAddresses(
        this Dhcpv6Message message,
        out IReadOnlyList<Dhcpv6IdentityAssociationForTemporaryAddresses> associations
    )
    {
        List<Dhcpv6IdentityAssociationForTemporaryAddresses> decoded = [];

        foreach (Dhcpv6Option option in FindOptions(message, Dhcpv6OptionCodeEnum.IaTa))
        {
            if (
                !Dhcpv6IdentityAssociationForTemporaryAddresses.TryDecode(
                    option.Data,
                    out Dhcpv6IdentityAssociationForTemporaryAddresses association,
                    out _
                )
            )
                continue;

            decoded.Add(association);
        }

        if (decoded.Count == 0)
        {
            associations = null;
            return false;
        }

        associations = decoded;
        return true;
    }

    /// <summary>
    ///     This method appends an Identity Association for Temporary Addresses (IA_TA, code 4)
    ///     option.
    /// </summary>
    public static void AddIdentityAssociationForTemporaryAddresses(
        this Dhcpv6Message message,
        Dhcpv6IdentityAssociationForTemporaryAddresses association
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(association);
        message.Options.Add(
            new Dhcpv6Option((ushort)Dhcpv6OptionCodeEnum.IaTa, association.Encode())
        );
    }

    /// <summary>
    ///     This method attempts to read every Identity Association for Prefix Delegation (IA_PD,
    ///     code 25) option in the message. A malformed IA_PD is skipped; the method returns
    ///     <see langword="false" /> when none decodes.
    /// </summary>
    public static bool TryGetIdentityAssociationsForPrefixDelegation(
        this Dhcpv6Message message,
        out IReadOnlyList<Dhcpv6IdentityAssociationForPrefixDelegation> associations
    )
    {
        List<Dhcpv6IdentityAssociationForPrefixDelegation> decoded = [];

        foreach (Dhcpv6Option option in FindOptions(message, Dhcpv6OptionCodeEnum.IaPd))
        {
            if (
                !Dhcpv6IdentityAssociationForPrefixDelegation.TryDecode(
                    option.Data,
                    out Dhcpv6IdentityAssociationForPrefixDelegation association,
                    out _
                )
            )
                continue;

            decoded.Add(association);
        }

        if (decoded.Count == 0)
        {
            associations = null;
            return false;
        }

        associations = decoded;
        return true;
    }

    /// <summary>
    ///     This method appends an Identity Association for Prefix Delegation (IA_PD, code 25)
    ///     option.
    /// </summary>
    public static void AddIdentityAssociationForPrefixDelegation(
        this Dhcpv6Message message,
        Dhcpv6IdentityAssociationForPrefixDelegation association
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(association);
        message.Options.Add(
            new Dhcpv6Option((ushort)Dhcpv6OptionCodeEnum.IaPd, association.Encode())
        );
    }

    /// <summary>
    ///     This method attempts to read any option's raw value by code, with no decoding, like
    ///     <see cref="Dhcpv4MessageOptionExtensions.TryGetRawOption" />.
    /// </summary>
    public static bool TryGetRawOption(this Dhcpv6Message message, ushort code, out byte[] data)
    {
        Dhcpv6Option option = FindOption(message, (Dhcpv6OptionCodeEnum)code);

        if (option is null)
        {
            data = null;
            return false;
        }

        data = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets any option's raw value by code, replacing an existing instance.
    ///     Custom options and tag-rule options (<c>Dhcpv6OptionOverlayService</c>) use it. A
    ///     <see langword="null" /> value is stored as empty.
    /// </summary>
    public static void SetRawOption(this Dhcpv6Message message, ushort code, byte[] data) =>
        SetOption(message, (Dhcpv6OptionCodeEnum)code, data ?? []);

    /// <summary>
    ///     This method attempts to read a non-empty raw option value, the shape of both DUID
    ///     options (Client and Server Identifier).
    /// </summary>
    private static bool TryGetNonEmptyRawOption(
        Dhcpv6Message message,
        Dhcpv6OptionCodeEnum code,
        out byte[] data
    )
    {
        Dhcpv6Option option = FindOption(message, code);

        if (option is not { Data.Length: > 0 })
        {
            data = null;
            return false;
        }

        data = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets a non-empty raw option value, throwing on an empty or missing value
    ///     rather than writing a zero-length DUID.
    /// </summary>
    private static void SetNonEmptyRawOption(
        Dhcpv6Message message,
        Dhcpv6OptionCodeEnum code,
        byte[] data,
        string parameterName
    )
    {
        if (data is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", parameterName);

        SetOption(message, code, data);
    }

    /// <summary>
    ///     This method finds the first option with a given code, or <see langword="null" /> if
    ///     there is none. IA_NA, IA_TA and IA_PD can repeat and use <see cref="FindOptions" />.
    /// </summary>
    private static Dhcpv6Option FindOption(Dhcpv6Message message, Dhcpv6OptionCodeEnum code)
    {
        ArgumentNullException.ThrowIfNull(message);
        ushort rawCode = (ushort)code;

        foreach (Dhcpv6Option option in message.Options)
            if (option.Code == rawCode)
                return option;

        return null;
    }

    /// <summary>
    ///     This method finds every option carrying a given code, in wire order.
    /// </summary>
    private static IEnumerable<Dhcpv6Option> FindOptions(
        Dhcpv6Message message,
        Dhcpv6OptionCodeEnum code
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ushort rawCode = (ushort)code;
        return message.Options.Where(option => option.Code == rawCode);
    }

    /// <summary>
    ///     This method replaces (or adds) the option with a given code. It is for single-instance
    ///     options; IA_NA, IA_TA and IA_PD are appended instead.
    /// </summary>
    private static void SetOption(Dhcpv6Message message, Dhcpv6OptionCodeEnum code, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(message);
        ushort rawCode = (ushort)code;
        message.Options.RemoveAll(option => option.Code == rawCode);
        message.Options.Add(new Dhcpv6Option(rawCode, data));
    }

    /// <summary>
    ///     This method decodes the Client FQDN Domain Name field (RFC 4704 Section 4.2). It cannot
    ///     reuse <see cref="DhcpDomainNameListCodec.TryDecode" />, which requires every name to end in a root
    ///     label so it can split a packed list. RFC 4704 allows three forms of the single name:
    ///     empty ("a client MAY ... leave the Domain Name field empty if it desires the server to
    ///     provide a name"), fully qualified ("including the terminating zero-length label"), and
    ///     partial ("without the terminating zero-length label").
    ///     <para>
    ///         <c>Ninlil.Dns.Common.Wire.DnsName</c> is not used because <c>Ninlil.Dhcp.Common</c>
    ///         does not reference <c>Ninlil.Dns.Common</c>, and that type cannot represent a
    ///         partial or empty name.
    ///     </para>
    /// </summary>
    /// <param name="data">The Domain Name field (the option's data without the leading flags octet).</param>
    /// <param name="domainName">
    ///     Receives the decoded, dot-joined domain name, or <see langword="null" /> when
    ///     <paramref name="data" /> was empty or decoded to the root name alone.
    /// </param>
    /// <param name="isFullyQualified">
    ///     Receives whether a terminating zero-length root label was present.
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if <paramref name="data" /> decoded successfully (including the
    ///     empty case); <see langword="false" /> for a truncated label, a compression pointer, or
    ///     trailing bytes found after an early root label.
    /// </returns>
    private static bool TryDecodeClientFqdnDomainName(
        byte[] data,
        out string domainName,
        out bool isFullyQualified
    )
    {
        domainName = null;
        isFullyQualified = false;

        if (data.Length == 0)
            return true;

        StringBuilder domain = new();
        int offset = 0;

        while (offset < data.Length)
        {
            byte labelLength = data[offset];
            offset++;

            // Compression pointers are not supported, as in DhcpDomainNameListCodec.TryDecode.
            if ((labelLength & 0xC0) != 0)
                return false;

            if (labelLength == 0)
            {
                isFullyQualified = true;
                break;
            }

            if (offset + labelLength > data.Length)
                return false;

            if (domain.Length > 0)
                domain.Append('.');

            domain.Append(Encoding.ASCII.GetString(data, offset, labelLength));
            offset += labelLength;
        }

        // This option carries one name, so bytes after the root label are trailing garbage.
        if (isFullyQualified && offset != data.Length)
        {
            domainName = null;
            isFullyQualified = false;
            return false;
        }

        domainName = domain.Length == 0 ? null : domain.ToString();
        return true;
    }

    /// <summary>
    ///     This method encodes the Client FQDN Domain Name field, the inverse of
    ///     <see cref="TryDecodeClientFqdnDomainName" /> (which explains why it is separate from
    ///     <see cref="DhcpDomainNameListCodec.TryEncode" />).
    /// </summary>
    /// <param name="domainName">The dot-joined domain name to encode, or <see langword="null" /> or empty for an empty field.</param>
    /// <param name="isFullyQualified">Whether to terminate the encoded name with a zero-length root label.</param>
    /// <returns>The encoded Domain Name field; empty when <paramref name="domainName" /> is <see langword="null" /> or empty.</returns>
    private static byte[] EncodeClientFqdnDomainName(string domainName, bool isFullyQualified)
    {
        if (string.IsNullOrEmpty(domainName))
            return [];

        using MemoryStream buffer = new();

        foreach (string label in domainName.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] labelBytes = Encoding.ASCII.GetBytes(label);

            if (labelBytes.Length > 63)
                throw new ArgumentException(
                    $"The label \"{label}\" is longer than the 63 bytes a DNS label can hold.",
                    nameof(domainName)
                );

            buffer.WriteByte((byte)labelBytes.Length);
            buffer.Write(labelBytes);
        }

        if (isFullyQualified)
            buffer.WriteByte(0);

        return buffer.ToArray();
    }
}
