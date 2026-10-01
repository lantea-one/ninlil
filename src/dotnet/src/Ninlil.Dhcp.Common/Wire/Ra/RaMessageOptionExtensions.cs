using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class provides typed accessors for the Neighbor Discovery options (RFC 4861, RFC 4191,
///     RFC 8106) used in Router Solicitations and Advertisements. It is the Router Advertisement
///     counterpart of <see cref="Dhcpv6MessageOptionExtensions" />: the message keeps one raw
///     options list, <c>TryGetX</c> returns <see langword="false" /> for a missing or malformed
///     option, <c>SetX</c> replaces an existing one, and the repeatable Prefix Information and
///     Route Information options are appended with <c>AddX</c>.
/// </summary>
public static class RaMessageOptionExtensions
{
    /// <summary>
    ///     This method attempts to read the Source Link-Layer Address option (RFC 4861 Section
    ///     4.6.1, type 1) a Router Solicitation carries.
    /// </summary>
    public static bool TryGetSourceLinkLayerAddress(
        this RouterSolicitationMessage message,
        out byte[] linkLayerAddress
    ) => TryGetSourceLinkLayerAddress(message.Options, out linkLayerAddress);

    /// <summary>
    ///     This method sets the Source Link-Layer Address option on a Router Solicitation.
    /// </summary>
    public static void SetSourceLinkLayerAddress(
        this RouterSolicitationMessage message,
        byte[] linkLayerAddress
    ) => SetSourceLinkLayerAddress(message.Options, linkLayerAddress);

    /// <summary>
    ///     This method attempts to read the Source Link-Layer Address option (RFC 4861 Section
    ///     4.6.1, type 1) of a Router Advertisement: the router's link-layer address.
    /// </summary>
    public static bool TryGetSourceLinkLayerAddress(
        this RouterAdvertisementMessage message,
        out byte[] linkLayerAddress
    ) => TryGetSourceLinkLayerAddress(message.Options, out linkLayerAddress);

    /// <summary>
    ///     This method sets the Source Link-Layer Address option on a Router Advertisement.
    /// </summary>
    public static void SetSourceLinkLayerAddress(
        this RouterAdvertisementMessage message,
        byte[] linkLayerAddress
    ) => SetSourceLinkLayerAddress(message.Options, linkLayerAddress);

    /// <summary>
    ///     This method attempts to read the MTU option (RFC 4861 Section 4.6.4, type 5).
    /// </summary>
    public static bool TryGetMtu(this RouterAdvertisementMessage message, out uint mtu)
    {
        RaOption option = FindOption(message.Options, (byte)RaOptionCodeEnum.Mtu);

        if (option is not { Data.Length: 6 })
        {
            mtu = 0;
            return false;
        }

        // The value is a 2-byte reserved field followed by the 4-byte MTU.
        mtu = BinaryPrimitives.ReadUInt32BigEndian(option.Data.AsSpan(2, 4));
        return true;
    }

    /// <summary>
    ///     This method sets the MTU option (RFC 4861 Section 4.6.4, type 5).
    /// </summary>
    public static void SetMtu(this RouterAdvertisementMessage message, uint mtu)
    {
        byte[] data = new byte[6];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(2, 4), mtu);
        SetOption(message.Options, (byte)RaOptionCodeEnum.Mtu, data);
    }

    /// <summary>
    ///     This method attempts to read every Prefix Information option (RFC 4861 Section 4.6.2,
    ///     type 3) in a Router Advertisement; there can be several. A malformed one is skipped,
    ///     and the method returns <see langword="false" /> when none decodes.
    /// </summary>
    public static bool TryGetPrefixInformationOptions(
        this RouterAdvertisementMessage message,
        out IReadOnlyList<RaPrefixInformationOption> prefixes
    )
    {
        List<RaPrefixInformationOption> decoded = [];

        foreach (
            RaOption option in message.Options.Where(candidate =>
                candidate.Type == (byte)RaOptionCodeEnum.PrefixInformation
            )
        )
        {
            if (
                !RaPrefixInformationOption.TryDecode(
                    option.Data,
                    out RaPrefixInformationOption prefix,
                    out _
                )
            )
                continue;

            decoded.Add(prefix);
        }

        if (decoded.Count == 0)
        {
            prefixes = null;
            return false;
        }

        prefixes = decoded;
        return true;
    }

    /// <summary>
    ///     This method appends a Prefix Information option. It appends rather than replaces,
    ///     since a message can carry several.
    /// </summary>
    public static void AddPrefixInformation(
        this RouterAdvertisementMessage message,
        RaPrefixInformationOption prefix
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(prefix);
        message.Options.Add(
            new RaOption((byte)RaOptionCodeEnum.PrefixInformation, prefix.Encode())
        );
    }

    /// <summary>
    ///     This method attempts to read every Route Information option (RFC 4191 Section 2.3,
    ///     type 24) in a Router Advertisement. Like <see cref="TryGetPrefixInformationOptions" />,
    ///     it skips malformed ones and returns <see langword="false" /> when none decodes.
    /// </summary>
    public static bool TryGetRouteInformationOptions(
        this RouterAdvertisementMessage message,
        out IReadOnlyList<RaRouteInformationOption> routes
    )
    {
        List<RaRouteInformationOption> decoded = [];

        foreach (
            RaOption option in message.Options.Where(candidate =>
                candidate.Type == (byte)RaOptionCodeEnum.RouteInformation
            )
        )
        {
            if (
                !RaRouteInformationOption.TryDecode(
                    option.Data,
                    out RaRouteInformationOption route,
                    out _
                )
            )
                continue;

            decoded.Add(route);
        }

        if (decoded.Count == 0)
        {
            routes = null;
            return false;
        }

        routes = decoded;
        return true;
    }

    /// <summary>
    ///     This method appends a Route Information option. It appends rather than replaces, since
    ///     a message can carry several.
    /// </summary>
    public static void AddRouteInformation(
        this RouterAdvertisementMessage message,
        RaRouteInformationOption route
    )
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(route);
        message.Options.Add(new RaOption((byte)RaOptionCodeEnum.RouteInformation, route.Encode()));
    }

    /// <summary>
    ///     This method attempts to read the Recursive DNS Server option (RFC 8106 Section 5.1,
    ///     type 25): one or more DNS resolver addresses and how long a host may use them.
    /// </summary>
    public static bool TryGetRecursiveDnsServers(
        this RouterAdvertisementMessage message,
        out TimeSpan lifetime,
        out IReadOnlyList<IPAddress> dnsServers
    )
    {
        RaOption option = FindOption(message.Options, (byte)RaOptionCodeEnum.RecursiveDnsServer);

        // RFC 8106 Section 5.1: a 2-byte reserved field, a 4-byte lifetime, then one or more
        // 16-byte addresses. At least one address is required.
        if (option is not { Data.Length: >= 22 } || (option.Data.Length - 6) % 16 != 0)
        {
            lifetime = default;
            dnsServers = null;
            return false;
        }

        lifetime = TimeSpan.FromSeconds(
            BinaryPrimitives.ReadUInt32BigEndian(option.Data.AsSpan(2, 4))
        );

        List<IPAddress> servers = new((option.Data.Length - 6) / 16);

        for (int offset = 6; offset < option.Data.Length; offset += 16)
            servers.Add(new IPAddress(option.Data.AsSpan(offset, 16).ToArray()));

        dnsServers = servers;
        return true;
    }

    /// <summary>
    ///     This method sets the Recursive DNS Server option (RFC 8106 Section 5.1, type 25). Every
    ///     address must be IPv6.
    /// </summary>
    public static void SetRecursiveDnsServers(
        this RouterAdvertisementMessage message,
        TimeSpan lifetime,
        IReadOnlyList<IPAddress> dnsServers
    )
    {
        if (dnsServers is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one DNS server address.",
                nameof(dnsServers)
            );

        byte[] data = new byte[6 + dnsServers.Count * 16];
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(2, 4), ToWireSeconds(lifetime));

        for (int i = 0; i < dnsServers.Count; i++)
        {
            if (dnsServers[i] is not { AddressFamily: AddressFamily.InterNetworkV6 } address)
                throw new ArgumentException(
                    "Every DNS server address must be an IPv6 address.",
                    nameof(dnsServers)
                );

            address.TryWriteBytes(data.AsSpan(6 + i * 16, 16), out _);
        }

        SetOption(message.Options, (byte)RaOptionCodeEnum.RecursiveDnsServer, data);
    }

    /// <summary>
    ///     This method attempts to read the DNS Search List option (RFC 8106 Section 5.2, type
    ///     31): one or more search domain suffixes and how long a host may use them. See
    ///     <see cref="RaDnsSearchListOption" /> for the compression-free name decoding.
    /// </summary>
    public static bool TryGetDnsSearchList(
        this RouterAdvertisementMessage message,
        out TimeSpan lifetime,
        out IReadOnlyList<string> domainNames
    )
    {
        RaOption option = FindOption(message.Options, (byte)RaOptionCodeEnum.DnsSearchList);

        if (
            option is null
            || !RaDnsSearchListOption.TryDecode(
                option.Data,
                out RaDnsSearchListOption decoded,
                out _
            )
        )
        {
            lifetime = default;
            domainNames = null;
            return false;
        }

        lifetime = decoded.Lifetime;
        domainNames = decoded.DomainNames;
        return true;
    }

    /// <summary>
    ///     This method sets the DNS Search List option (RFC 8106 Section 5.2, type 31).
    /// </summary>
    public static void SetDnsSearchList(
        this RouterAdvertisementMessage message,
        TimeSpan lifetime,
        IReadOnlyList<string> domainNames
    )
    {
        if (domainNames is not { Count: > 0 })
            throw new ArgumentException(
                "Must contain at least one DNS search domain.",
                nameof(domainNames)
            );

        RaDnsSearchListOption option = new()
        {
            Lifetime = lifetime,
            DomainNames = [.. domainNames],
        };
        SetOption(message.Options, (byte)RaOptionCodeEnum.DnsSearchList, option.Encode());
    }

    /// <summary>
    ///     This method attempts to read a Source Link-Layer Address option's raw address bytes,
    ///     for both message types.
    /// </summary>
    private static bool TryGetSourceLinkLayerAddress(
        List<RaOption> options,
        out byte[] linkLayerAddress
    )
    {
        RaOption option = FindOption(options, (byte)RaOptionCodeEnum.SourceLinkLayerAddress);

        if (option is not { Data.Length: > 0 })
        {
            linkLayerAddress = null;
            return false;
        }

        linkLayerAddress = option.Data;
        return true;
    }

    /// <summary>
    ///     This method sets a Source Link-Layer Address option's raw address bytes, for both
    ///     message types. The bytes are not padded, so their length plus the 2-byte header must
    ///     be a multiple of 8 (a 6-byte MAC fits) or <see cref="RaMessageWriter" /> throws.
    /// </summary>
    private static void SetSourceLinkLayerAddress(List<RaOption> options, byte[] linkLayerAddress)
    {
        if (linkLayerAddress is not { Length: > 0 })
            throw new ArgumentException("Must be at least 1 byte.", nameof(linkLayerAddress));

        SetOption(options, (byte)RaOptionCodeEnum.SourceLinkLayerAddress, linkLayerAddress);
    }

    /// <summary>
    ///     This method converts an RDNSS lifetime to unsigned 32-bit seconds, writing a value at
    ///     or above <see cref="uint.MaxValue" /> seconds as infinity. A negative value throws.
    /// </summary>
    private static uint ToWireSeconds(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "An RDNSS lifetime cannot be negative."
            );

        double totalSeconds = lifetime.TotalSeconds;
        return totalSeconds >= uint.MaxValue ? uint.MaxValue : (uint)totalSeconds;
    }

    /// <summary>
    ///     This method finds the first option with a given type, or <see langword="null" /> if
    ///     there is none. The repeatable Prefix Information and Route Information options use a
    ///     <c>Where</c> filter instead.
    /// </summary>
    private static RaOption FindOption(List<RaOption> options, byte type) =>
        options.FirstOrDefault(option => option.Type == type);

    /// <summary>
    ///     This method replaces (or adds) the option with a given type. It is for single-instance
    ///     options; Prefix Information and Route Information are appended instead.
    /// </summary>
    private static void SetOption(List<RaOption> options, byte type, byte[] data)
    {
        options.RemoveAll(option => option.Type == type);
        options.Add(new RaOption(type, data));
    }
}
