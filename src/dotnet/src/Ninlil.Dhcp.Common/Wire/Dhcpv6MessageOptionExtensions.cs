using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire;

/// <summary>
///     This class provides strongly-typed accessors for the handful of RFC 8415 (plus RFC 3646)
///     options a DHCPv6 exchange cannot function without, layered on top of the raw
///     <see cref="Dhcpv6Option" /> tag/length/value list every <see cref="Dhcpv6Message" />
///     carries in <see cref="Dhcpv6Message.Options" /> -- the DHCPv6 analogue of
///     <see cref="Dhcpv4MessageOptionExtensions" />. Most options here follow that same small
///     shared-primitive shape; the IA_NA/IA_TA/IA_PD family is the one genuine structural
///     departure DHCPv6 introduces over DHCPv4 -- each is its own small container type
///     (<see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses" /> and siblings) with its
///     own dedicated encode/decode, and because a message can carry more than one of a given kind
///     (a client requesting several addresses/prefixes at once), those accessors return/append a
///     list rather than get/set a single value.
/// </summary>
public static class Dhcpv6MessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the Client Identifier option's own DUID bytes (code 1).
    ///     See <see cref="Dhcpv6OptionCodeEnum.ClientIdentifier" />'s own doc comment for why this
    ///     project treats a DUID as opaque.
    /// </summary>
    public static bool TryGetClientIdentifier(this Dhcpv6Message message, out byte[] duid) =>
        TryGetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ClientIdentifier, out duid);

    /// <summary>
    ///     This method sets the Client Identifier option (code 1).
    /// </summary>
    public static void SetClientIdentifier(this Dhcpv6Message message, byte[] duid) =>
        SetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ClientIdentifier, duid, nameof(duid));

    /// <summary>
    ///     This method attempts to read the Server Identifier option's own DUID bytes (code 2).
    /// </summary>
    public static bool TryGetServerIdentifier(this Dhcpv6Message message, out byte[] duid) =>
        TryGetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ServerIdentifier, out duid);

    /// <summary>
    ///     This method sets the Server Identifier option (code 2).
    /// </summary>
    public static void SetServerIdentifier(this Dhcpv6Message message, byte[] duid) =>
        SetNonEmptyRawOption(message, Dhcpv6OptionCodeEnum.ServerIdentifier, duid, nameof(duid));

    /// <summary>
    ///     This method attempts to read the Option Request Option (code 6) -- the list of other
    ///     option codes a client is asking a server to include in its reply, if the server has
    ///     values for them.
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
    ///     21.9, the wire value is hundredths of a second, capped at 0xFFFF (about 655.35
    ///     seconds) to mean "more than that" -- this method reports the capped value as-is rather
    ///     than trying to distinguish an exact 655.35-second elapsed time from a longer one the
    ///     wire format cannot represent precisely.
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
    ///     This method sets the Elapsed Time option (code 8), clamping a value that would not fit
    ///     the wire format's own 16-bit hundredths-of-a-second field to its maximum
    ///     representable value rather than throwing -- RFC 8415 Section 21.9 defines that exact
    ///     clamped value ("more than 655.35 seconds") as meaningful, not an error.
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
    ///     This method attempts to read the Status Code option (code 13) carried at this
    ///     message's own top level. A Status Code option nested inside a specific IA_NA/IA_TA/
    ///     IA_PD is reached through that association's own <see cref="Dhcpv6IdentityAssociationForNonTemporaryAddresses.Options" />/
    ///     equivalent list instead, via <see cref="TryGetStatusCode(IReadOnlyList{Dhcpv6Option},out Dhcpv6StatusCodeEnum,out string)" />.
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
    ///     This method attempts to read a Status Code option (code 13) out of an arbitrary nested
    ///     options list -- an IA_NA/IA_TA/IA_PD association's own <c>Options</c> property.
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
    ///     This method decodes a Status Code option's own value bytes, shared by both of this
    ///     class's own public <c>TryGetStatusCode</c> overloads above.
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
    ///     This method sets the Status Code option (code 13) at this message's own top level.
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
    ///     This method encodes a Status Code option's own value bytes -- shared by
    ///     <see cref="SetStatusCode(Dhcpv6Message,Dhcpv6StatusCodeEnum,string)" /> and by any
    ///     future caller building a nested Status Code option for an IA_NA/IA_TA/IA_PD
    ///     association's own <c>Options</c> list directly.
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
    ///     This method attempts to read the Rapid Commit option (code 14) -- a zero-length
    ///     presence marker, so its only meaningful signal is whether it is present at all.
    /// </summary>
    public static bool TryGetRapidCommit(this Dhcpv6Message message) =>
        FindOption(message, Dhcpv6OptionCodeEnum.RapidCommit) is not null;

    /// <summary>
    ///     This method sets the Rapid Commit option (code 14).
    /// </summary>
    public static void SetRapidCommit(this Dhcpv6Message message) =>
        SetOption(message, Dhcpv6OptionCodeEnum.RapidCommit, []);

    /// <summary>
    ///     This method attempts to read the User Class option (RFC 8415 Section 21.15, code 15) --
    ///     one or more "user-class-data" instances packed back-to-back, each its own 2-octet length
    ///     followed by that many octets of opaque text, decoded as ASCII the same way this project
    ///     already treats DHCPv4's structurally near-identical User Class option
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
            // Each instance is its own 2-octet length field (unlike DHCPv4's 1-octet field)
            // followed by that many octets of opaque data.
            if (offset + 2 > option.Data.Length)
                break;

            ushort length = BinaryPrimitives.ReadUInt16BigEndian(option.Data.AsSpan(offset, 2));
            offset += 2;

            // A malformed instance (its own declared length runs past the end of the option's
            // data) is not sensible to partially trust -- stop, keeping whatever whole instances
            // were already decoded rather than throwing away the entire option, mirroring
            // Dhcpv4MessageOptionExtensions.TryGetUserClasses's own identical guard.
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
    ///     This method attempts to read the Vendor Class option (RFC 8415 Section 21.16, code 16)
    ///     -- a 4-octet IANA Enterprise Number followed by one or more "vendor-class-data"
    ///     instances, each its own 2-octet length followed by that many octets of opaque text.
    /// </summary>
    /// <param name="message">The message to read the option from.</param>
    /// <param name="enterpriseNumber">Receives the vendor's own IANA Enterprise Number.</param>
    /// <param name="vendorClassData">Receives every decoded vendor-class-data instance, in wire order.</param>
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

            // Same "stop, keep what already decoded" tolerance TryGetUserClasses applies.
            if (length == 0 || offset + length > option.Data.Length)
                break;

            result.Add(Encoding.ASCII.GetString(option.Data, offset, length));
            offset += length;
        }

        // The enterprise number alone (with zero vendor-class-data instances) is a degenerate but
        // still meaningful wire shape -- RFC 8415 Section 21.16 does not require at least one
        // instance the way it does for the Vendor-specific Information option's own sub-options,
        // so this is reported successfully with an empty list rather than failing outright.
        vendorClassData = result;
        return true;
    }

    /// <summary>
    ///     This method sets the Vendor Class option (RFC 8415 Section 21.16, code 16).
    /// </summary>
    /// <param name="message">The message to set the option on.</param>
    /// <param name="enterpriseNumber">The vendor's own IANA Enterprise Number.</param>
    /// <param name="vendorClassData">Every vendor-class-data instance to encode, in order -- may be empty.</param>
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
    ///     code 23) -- one or more recursive DNS server addresses, in the server's own order of
    ///     preference.
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
    ///     This method attempts to read the Domain Search List option (RFC 3646 Section 3.1, code
    ///     24) -- one or more domain names for a client to search when resolving an unqualified
    ///     host name. This project decodes only ordinary, uncompressed RFC 1035 label sequences;
    ///     a name using RFC 1035 Section 4.1.4 compression (a pointer back into the option's own
    ///     bytes, or beyond them into the rest of the message) is rejected rather than resolved --
    ///     compression is optional per RFC 3646, and resolving a pointer correctly would need
    ///     access to the whole message's own bytes, not just this one option's, which this
    ///     project's per-option decode does not have.
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

        if (!TryDecodeDomainNames(option.Data, out List<string> decoded))
        {
            domains = null;
            return false;
        }

        domains = decoded;
        return true;
    }

    /// <summary>
    ///     This method sets the Domain Search List option (RFC 3646 Section 3.1, code 24), always
    ///     writing plain, uncompressed RFC 1035 label sequences (see <see cref="TryGetDomainSearchList" />'s
    ///     own doc comment for why this project never emits compression either).
    /// </summary>
    public static void SetDomainSearchList(
        this Dhcpv6Message message,
        IReadOnlyList<string> domains
    )
    {
        if (domains is not { Count: > 0 })
            throw new ArgumentException("Must contain at least one domain name.", nameof(domains));

        using MemoryStream buffer = new();

        foreach (string domain in domains)
            EncodeDomainName(domain, buffer);

        SetOption(message, Dhcpv6OptionCodeEnum.DomainSearchList, buffer.ToArray());
    }

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "S" bit -- the lowest-order bit of the
    ///     Client FQDN option's own single flags octet, per that section's own field diagram
    ///     (<c>0 1 2 3 4 5 6 7 | MBZ |N|O|S|</c>, bit 0 leftmost/high-order, bit 7 rightmost/
    ///     low-order). Quoting Section 4.1 directly: "The 'S' bit indicates whether the server
    ///     SHOULD or SHOULD NOT perform the AAAA RR (FQDN-to-address) DNS updates ... The state of
    ///     the bit in the reply from the server indicates the action to be taken by the server; if
    ///     it is 1, the server has taken responsibility for AAAA RR updates for the FQDN."
    /// </summary>
    private const byte ClientFqdnServerPerformsAaaaUpdateBit = 0x01;

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "O" bit -- the middle bit of the same
    ///     flags octet. Quoting Section 4.1 directly: "The 'O' bit indicates whether the server has
    ///     overridden the client's preference for the 'S' bit. A client MUST set this bit to 0. A
    ///     server MUST set this bit to 1 if the 'S' bit in its reply to the client does not match
    ///     the 'S' bit received from the client."
    /// </summary>
    private const byte ClientFqdnServerOverrodeClientPreferenceBit = 0x02;

    /// <summary>
    ///     This constant contains the RFC 4704 Section 4.1 "N" bit -- the highest-order bit of the
    ///     same flags octet. Quoting Section 4.1 directly: "The 'N' bit indicates whether the
    ///     server SHOULD NOT perform any DNS updates ... A server sets the 'N' bit to indicate
    ///     whether the server SHALL (0) or SHALL NOT (1) perform DNS updates. If the 'N' bit is 1,
    ///     the 'S' bit MUST be 0."
    /// </summary>
    private const byte ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit = 0x04;

    /// <summary>
    ///     This method attempts to read the Client FQDN option (RFC 4704, code 39) -- a client's
    ///     own presented (fully-qualified or partial) domain name, plus the N/O/S flag bits (see
    ///     <see cref="ClientFqdnServerShouldNotPerformAnyDnsUpdatesBit" />/
    ///     <see cref="ClientFqdnServerOverrodeClientPreferenceBit" />/
    ///     <see cref="ClientFqdnServerPerformsAaaaUpdateBit" />'s own doc comments for the exact
    ///     RFC 4704 Section 4.1 semantics of each). This same accessor decodes both a client's
    ///     request and a server's own reply -- the wire shape is identical either direction, only
    ///     the meaning of each flag's sender changes.
    /// </summary>
    /// <param name="message">The message to read the option from.</param>
    /// <param name="serverShouldNotPerformAnyDnsUpdates">Receives the "N" bit.</param>
    /// <param name="serverOverrodeClientPreference">Receives the "O" bit.</param>
    /// <param name="serverPerformsAaaaUpdate">Receives the "S" bit.</param>
    /// <param name="domainName">
    ///     Receives the decoded domain name (dot-joined labels), or <see langword="null" /> when
    ///     the Domain Name field was left empty -- RFC 4704 Section 4.2's own signal that "a client
    ///     ... desires the server to provide a name."
    /// </param>
    /// <param name="isFullyQualified">
    ///     Receives whether the Domain Name field carried a terminating zero-length root label
    ///     (RFC 4704 Section 4.2's "fully qualified" form) rather than ending mid-name (that
    ///     section's own "partial name" form, sent "without the terminating zero-length label").
    /// </param>
    /// <returns>
    ///     <see langword="true" /> if the option was present and its Domain Name field decoded
    ///     successfully; <see langword="false" /> if the option is absent, or its Domain Name
    ///     field is malformed (a truncated label, or an RFC 1035 Section 4.1.4 compression pointer
    ///     -- this project decodes only plain, uncompressed label sequences here, the same
    ///     restriction <see cref="TryGetDomainSearchList" /> already documents for RFC 3646's own
    ///     Domain Search List option).
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
    ///     This method sets the Client FQDN option (RFC 4704, code 39), used both by a client
    ///     building a request and (this project's own only real use) a server building its own
    ///     reply -- see <see cref="TryGetClientFqdn" />'s own doc comment for the shared wire shape.
    /// </summary>
    /// <param name="message">The message to set the option on.</param>
    /// <param name="serverShouldNotPerformAnyDnsUpdates">The "N" bit to encode.</param>
    /// <param name="serverOverrodeClientPreference">The "O" bit to encode.</param>
    /// <param name="serverPerformsAaaaUpdate">The "S" bit to encode.</param>
    /// <param name="domainName">
    ///     The domain name to encode (dot-joined labels), or <see langword="null" />/empty to
    ///     write an empty Domain Name field (RFC 4704 Section 4.2's "the server provides no name"
    ///     counterpart to a client leaving the field empty to ask for one).
    /// </param>
    /// <param name="isFullyQualified">
    ///     Whether to terminate the encoded name with a zero-length root label (a fully-qualified
    ///     name) or leave it unterminated (a partial name) -- ignored when
    ///     <paramref name="domainName" /> is <see langword="null" />/empty.
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
    ///     (IA_NA, code 3) option this message carries.
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
    ///     This method appends one more Identity Association for Non-temporary Addresses (IA_NA,
    ///     code 3) option -- appends, rather than replaces, since a message can legitimately carry
    ///     more than one (a client requesting several non-temporary addresses at once).
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
    ///     This method attempts to read every Identity Association for Temporary Addresses
    ///     (IA_TA, code 4) option this message carries.
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
    ///     This method appends one more Identity Association for Temporary Addresses (IA_TA,
    ///     code 4) option.
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
    ///     code 25) option this message carries.
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
    ///     This method appends one more Identity Association for Prefix Delegation (IA_PD, code
    ///     25) option.
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
    ///     This method attempts to read an arbitrary option's raw value bytes by its own numeric
    ///     code, with no type-specific decoding -- the same escape hatch
    ///     <see cref="Dhcpv4MessageOptionExtensions.TryGetRawOption" /> provides for DHCPv4, kept
    ///     available here for whichever future DHCPv6 server engine/option-delivery feature needs
    ///     it.
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
    ///     This method sets (replacing any existing instance of the same code) an arbitrary
    ///     option's raw value bytes by its own numeric code.
    /// </summary>
    public static void SetRawOption(this Dhcpv6Message message, ushort code, byte[] data) =>
        SetOption(message, (Dhcpv6OptionCodeEnum)code, data ?? []);

    /// <summary>
    ///     This method attempts to read a non-empty raw option value -- the shape both DUID
    ///     options (Client/Server Identifier) share.
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
    ///     This method sets a non-empty raw option value, throwing on an empty/missing value
    ///     rather than silently writing a meaningless zero-length DUID.
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
    ///     This method finds the first option carrying a given code, or <see langword="null" />
    ///     if the message carries none -- correct for every option this class gives a typed
    ///     accessor to except IA_NA/IA_TA/IA_PD, which use <see cref="FindOptions" /> instead
    ///     since a message can carry more than one of those.
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
    ///     This method replaces (or adds, if absent) the option carrying a given code. Used only
    ///     by the single-instance options above -- IA_NA/IA_TA/IA_PD are always appended directly
    ///     instead, since a message can carry more than one.
    /// </summary>
    private static void SetOption(Dhcpv6Message message, Dhcpv6OptionCodeEnum code, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(message);
        ushort rawCode = (ushort)code;
        message.Options.RemoveAll(option => option.Code == rawCode);
        message.Options.Add(new Dhcpv6Option(rawCode, data));
    }

    /// <summary>
    ///     This method decodes one or more RFC 1035-style, length-prefixed domain names packed
    ///     back-to-back, each terminated by a zero-length root label, with no compression
    ///     supported (see <see cref="TryGetDomainSearchList" />'s own doc comment for why).
    /// </summary>
    private static bool TryDecodeDomainNames(byte[] data, out List<string> domains)
    {
        domains = [];
        int offset = 0;

        while (offset < data.Length)
        {
            StringBuilder domain = new();
            bool sawRootLabel = false;

            while (offset < data.Length)
            {
                byte labelLength = data[offset];
                offset++;

                // The top two bits set would mean an RFC 1035 Section 4.1.4 compression pointer,
                // which this method deliberately does not support (see this class's own
                // TryGetDomainSearchList doc comment).
                if ((labelLength & 0xC0) != 0)
                    return false;

                if (labelLength == 0)
                {
                    sawRootLabel = true;
                    break;
                }

                if (offset + labelLength > data.Length)
                    return false;

                if (domain.Length > 0)
                    domain.Append('.');

                domain.Append(Encoding.ASCII.GetString(data, offset, labelLength));
                offset += labelLength;
            }

            if (!sawRootLabel)
                return false;

            domains.Add(domain.ToString());
        }

        return domains.Count > 0;
    }

    /// <summary>
    ///     This method encodes one RFC 1035-style, length-prefixed domain name, terminated by a
    ///     zero-length root label, into <paramref name="buffer" />.
    /// </summary>
    private static void EncodeDomainName(string domain, MemoryStream buffer)
    {
        if (string.IsNullOrWhiteSpace(domain))
            throw new ArgumentException("A domain name cannot be empty.", nameof(domain));

        foreach (string label in domain.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            byte[] labelBytes = Encoding.ASCII.GetBytes(label);

            if (labelBytes.Length > 63)
                throw new ArgumentException(
                    $"The label \"{label}\" is longer than the 63 bytes a DNS label can hold.",
                    nameof(domain)
                );

            buffer.WriteByte((byte)labelBytes.Length);
            buffer.Write(labelBytes);
        }

        buffer.WriteByte(0);
    }

    /// <summary>
    ///     This method decodes the Client FQDN option's own Domain Name field (RFC 4704 Section
    ///     4.2), which needs its own decoder distinct from <see cref="TryDecodeDomainNames" />
    ///     rather than reusing it: that method always requires every name it decodes to end in a
    ///     zero-length root label (correct for RFC 3646's Domain Search List, which packs several
    ///     complete names back-to-back and needs that terminator to know where one name ends and
    ///     the next begins), where RFC 4704 Section 4.2 instead defines three genuinely different,
    ///     meaningful shapes for this option's own single name: empty (zero bytes -- "a client MAY
    ///     ... leave the Domain Name field empty if it desires the server to provide a name"), a
    ///     terminated "fully qualified" name ("the Domain Name field is set to the DNS-encoded
    ///     domain name including the terminating zero-length label"), and an unterminated "partial"
    ///     name ("the Domain Name field is set to the DNS-encoded domain name without the
    ///     terminating zero-length label"). Reusing <c>Ninlil.Dns.Common.Wire.DnsName</c>
    ///     was considered instead of this small local decoder, but rejected for two reasons: doing
    ///     so would give <c>Ninlil.Dhcp.Common</c> a project reference onto <c>Ninlil.Dns.Common</c>
    ///     it has never needed before (this project's DHCP wire layer is deliberately as
    ///     dependency-free as its DNS counterpart, not a consumer of it), and that class's own model
    ///     -- a name is always a complete, well-formed <c>Ninlil.Dns.Common.Wire.DnsName</c>
    ///     instance -- has no representation at all for this option's own genuinely valid "partial,
    ///     unterminated name" and "empty, no name at all" wire shapes without first wrapping it in
    ///     new code anyway, at which point a small, self-contained decoder mirroring this file's own
    ///     existing <see cref="TryDecodeDomainNames" />/<see cref="EncodeDomainName" /> precedent
    ///     for RFC 3646's option is the more consistent choice.
    /// </summary>
    /// <param name="data">The Domain Name field's own raw bytes (the option's data, minus its leading flags octet).</param>
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

            // See TryDecodeDomainNames's own identical guard -- this project never decodes an
            // RFC 1035 Section 4.1.4 compression pointer for a DHCPv6 option.
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

        // A root label found before the data actually ends means genuine trailing garbage after
        // the name -- this option carries exactly one name, never several packed back-to-back the
        // way RFC 3646's Domain Search List does.
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
    ///     This method encodes the Client FQDN option's own Domain Name field -- the inverse of
    ///     <see cref="TryDecodeClientFqdnDomainName" />, sharing that method's own doc comment for
    ///     why this option needs its own encoder distinct from <see cref="EncodeDomainName" />.
    /// </summary>
    /// <param name="domainName">The dot-joined domain name to encode, or <see langword="null" />/empty for an empty field.</param>
    /// <param name="isFullyQualified">Whether to terminate the encoded name with a zero-length root label.</param>
    /// <returns>The encoded Domain Name field bytes -- zero-length when <paramref name="domainName" /> is <see langword="null" />/empty.</returns>
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
