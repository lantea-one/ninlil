using System.Net;
using System.Net.Sockets;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Ra;

/// <summary>
///     This class is the one source of the limits a Router Advertisement's content must respect.
///     The settings controller uses it to reject a bad value with a message that names the field;
///     <see cref="RaAdvertisementProvider" /> uses it to skip or clamp a bad value that reached
///     the configuration file by hand, so building an advertisement never throws. Every
///     <c>Check*</c> method returns <see langword="null" /> when the value is acceptable and an
///     error message otherwise.
/// </summary>
public static class RaContentRules
{
    /// <summary>
    ///     This field holds the largest Router Lifetime in seconds: the field is 16 bits wide
    ///     (RFC 4861 Section 4.2; RFC 8319 Section 4 raises the old 9000 s cap to the field width).
    /// </summary>
    public const int MaxRouterLifetimeSeconds = ushort.MaxValue;

    /// <summary>
    ///     This field holds the largest Reachable Time in milliseconds (RFC 4861 Section 6.2.1,
    ///     MAX_REACHABLE_TIME: one hour).
    /// </summary>
    public const uint MaxReachableTimeMilliseconds = 3_600_000;

    /// <summary>
    ///     This field holds the smallest link MTU an MTU option may carry (RFC 8200 Section 5).
    /// </summary>
    public const uint MinLinkMtu = 1280;

    /// <summary>
    ///     This field holds the most addresses one RDNSS option can carry: its length byte counts
    ///     8-octet units, 1 + 2n units must fit in 255, so n is at most 127 (RFC 8106 Section 5.1).
    /// </summary>
    public const int MaxRecursiveDnsServers = 127;

    /// <summary>
    ///     This field holds the smallest allowed unsolicited advertisement interval in seconds
    ///     (RFC 4861 Section 6.2.1, MinRtrAdvInterval lower bound).
    /// </summary>
    public const int MinIntervalSeconds = 3;

    /// <summary>
    ///     This field holds the smallest allowed maximum advertisement interval in seconds (RFC
    ///     4861 Section 6.2.1, MaxRtrAdvInterval lower bound).
    /// </summary>
    public const int MinMaxIntervalSeconds = 4;

    /// <summary>
    ///     This field holds the largest allowed maximum advertisement interval in seconds. RFC
    ///     4861 Section 6.2.1 caps MaxRtrAdvInterval at 1800 s; the limit here is the 16-bit
    ///     Router Lifetime width, since RFC 8319 Section 4 lets a router lifetime reach it.
    /// </summary>
    public const int MaxMaxIntervalSeconds = ushort.MaxValue;

    /// <summary>
    ///     This field holds the largest ratio of the minimum to the maximum interval (RFC 4861
    ///     Section 6.2.1: MinRtrAdvInterval must be at most 0.75 times MaxRtrAdvInterval).
    /// </summary>
    public const double MaxMinToMaxRatio = 0.75;

    /// <summary>
    ///     This method checks a Router Lifetime: 0 to 65 535 seconds.
    /// </summary>
    public static string CheckRouterLifetime(TimeSpan value, string field = "routerLifetime")
    {
        if (value < TimeSpan.Zero)
            return $"'{field}' must not be negative.";

        return value.TotalSeconds > MaxRouterLifetimeSeconds
            ? $"'{field}' must be at most {TimeSpan.FromSeconds(MaxRouterLifetimeSeconds)} "
                + $"({MaxRouterLifetimeSeconds} seconds)."
            : null;
    }

    /// <summary>
    ///     This method checks a Reachable Time: 0 to one hour.
    /// </summary>
    public static string CheckReachableTime(TimeSpan value, string field = "reachableTime")
    {
        if (value < TimeSpan.Zero)
            return $"'{field}' must not be negative.";

        return value.TotalMilliseconds > MaxReachableTimeMilliseconds
            ? $"'{field}' must be at most "
                + $"{TimeSpan.FromMilliseconds(MaxReachableTimeMilliseconds)} "
                + $"({MaxReachableTimeMilliseconds} milliseconds)."
            : null;
    }

    /// <summary>
    ///     This method checks a Retrans Timer: 0 to <see cref="uint.MaxValue" /> milliseconds.
    /// </summary>
    public static string CheckRetransTimer(TimeSpan value, string field = "retransTimer")
    {
        if (value < TimeSpan.Zero)
            return $"'{field}' must not be negative.";

        return value.TotalMilliseconds > uint.MaxValue
            ? $"'{field}' must be at most {uint.MaxValue} milliseconds."
            : null;
    }

    /// <summary>
    ///     This method checks a lifetime that only has to be non-negative (RDNSS, DNSSL, route
    ///     and prefix lifetimes; values above 32 bits mean infinity on the wire).
    /// </summary>
    public static string CheckLifetime(TimeSpan value, string field) =>
        value < TimeSpan.Zero ? $"'{field}' must not be negative." : null;

    /// <summary>
    ///     This method checks a link MTU: absent, or at least <see cref="MinLinkMtu" />.
    /// </summary>
    public static string CheckLinkMtu(uint? value, string field = "linkMtu") =>
        value is < MinLinkMtu
            ? $"'{field}' must be at least {MinLinkMtu} (the IPv6 minimum link MTU)."
            : null;

    /// <summary>
    ///     This method checks one RDNSS address: IPv6, and neither unspecified, multicast nor an
    ///     IPv4-mapped address.
    /// </summary>
    public static string CheckRecursiveDnsServer(string text)
    {
        if (!IPAddress.TryParse(text, out IPAddress address))
            return $"'{text}' is not a valid IP address.";

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv4MappedToIPv6)
            return $"'{text}' is not an IPv6 address; a Router Advertisement carries IPv6 DNS "
                + "servers only.";

        if (address.Equals(IPAddress.IPv6Any) || address.IsIPv6Multicast)
            return $"'{text}' is not a usable DNS server address.";

        return null;
    }

    /// <summary>
    ///     This method checks the number of RDNSS addresses: at most
    ///     <see cref="MaxRecursiveDnsServers" />.
    /// </summary>
    public static string CheckRecursiveDnsServerCount(int count) =>
        count > MaxRecursiveDnsServers
            ? $"'recursiveDnsServers' must hold at most {MaxRecursiveDnsServers} addresses."
            : null;

    /// <summary>
    ///     This method checks a prefix length: 0 to 128.
    /// </summary>
    public static string CheckPrefixLength(int length) =>
        length is < 0 or > 128
            ? $"'{length}' is not a valid prefix length -- it must be between 0 and 128."
            : null;

    /// <summary>
    ///     This method checks a prefix to advertise for SLAAC: an IPv6 address that is neither
    ///     multicast nor link-local (RFC 4861 Section 6.2.1: the link-local prefix SHOULD NOT be
    ///     advertised), with a length of 0 to 128.
    /// </summary>
    public static string CheckPrefix(string text, int length) =>
        CheckPrefixLength(length) ?? CheckPrefixAddress(text, allowLinkLocal: false, out _);

    /// <summary>
    ///     This method checks a route prefix: as <see cref="CheckPrefix" />, but link-local is
    ///     allowed and a length of 0 needs no address.
    /// </summary>
    public static string CheckRoutePrefix(string text, int length)
    {
        string lengthError = CheckPrefixLength(length);
        if (lengthError is not null)
            return lengthError;

        return length == 0 ? null : CheckPrefixAddress(text, allowLinkLocal: true, out _);
    }

    /// <summary>
    ///     This method checks that a prefix's preferred lifetime does not exceed its valid
    ///     lifetime; hosts ignore the option otherwise (RFC 4862 Section 5.5.3 (c)).
    /// </summary>
    public static string CheckPrefixLifetimes(TimeSpan validLifetime, TimeSpan preferredLifetime)
    {
        string error =
            CheckLifetime(validLifetime, "validLifetime")
            ?? CheckLifetime(preferredLifetime, "preferredLifetime");

        if (error is not null)
            return error;

        return preferredLifetime > validLifetime
            ? "'preferredLifetime' must not exceed 'validLifetime'."
            : null;
    }

    /// <summary>
    ///     This method validates a prefix to advertise and clears its host bits, so
    ///     <c>2001:db8::1/64</c> becomes <c>2001:db8::</c>. It returns <see langword="false" />
    ///     when <see cref="CheckPrefix" /> would reject the pair.
    /// </summary>
    public static bool TryNormalizePrefix(string text, int length, out IPAddress masked)
    {
        masked = null;

        if (
            CheckPrefixLength(length) is not null
            || CheckPrefixAddress(text, allowLinkLocal: false, out IPAddress address) is not null
        )
            return false;

        masked = Mask(address, length);
        return true;
    }

    /// <summary>
    ///     This method validates a route prefix and clears its host bits. A length of 0 yields
    ///     <see cref="IPAddress.IPv6Any" /> whatever the text says.
    /// </summary>
    public static bool TryNormalizeRoutePrefix(string text, int length, out IPAddress masked)
    {
        masked = null;

        if (CheckPrefixLength(length) is not null)
            return false;

        if (length == 0)
        {
            masked = IPAddress.IPv6Any;
            return true;
        }

        if (CheckPrefixAddress(text, allowLinkLocal: true, out masked) is not null)
            return false;

        masked = Mask(masked, length);
        return true;
    }

    /// <summary>
    ///     This method checks the advertisement interval pair: the minimum at least
    ///     <see cref="MinIntervalSeconds" /> seconds, the maximum between
    ///     <see cref="MinMaxIntervalSeconds" /> and <see cref="MaxMaxIntervalSeconds" /> seconds,
    ///     and the minimum at most <see cref="MaxMinToMaxRatio" /> times the maximum.
    /// </summary>
    public static string CheckIntervals(TimeSpan minimum, TimeSpan maximum)
    {
        if (minimum.TotalSeconds < MinIntervalSeconds)
            return $"'minRouterAdvertisementInterval' must be at least {MinIntervalSeconds} "
                + "seconds.";

        if (maximum.TotalSeconds < MinMaxIntervalSeconds)
            return $"'maxRouterAdvertisementInterval' must be at least {MinMaxIntervalSeconds} "
                + "seconds.";

        if (maximum.TotalSeconds > MaxMaxIntervalSeconds)
            return $"'maxRouterAdvertisementInterval' must be at most "
                + $"{TimeSpan.FromSeconds(MaxMaxIntervalSeconds)} ({MaxMaxIntervalSeconds} "
                + "seconds).";

        return minimum.TotalSeconds > MaxMinToMaxRatio * maximum.TotalSeconds
            ? $"'minRouterAdvertisementInterval' ({minimum}) must be at most "
                + $"{MaxMinToMaxRatio} times 'maxRouterAdvertisementInterval' ({maximum})."
            : null;
    }

    /// <summary>
    ///     This method parses a prefix address and applies the family and range rules shared by
    ///     prefixes and routes.
    /// </summary>
    private static string CheckPrefixAddress(
        string text,
        bool allowLinkLocal,
        out IPAddress address
    )
    {
        if (!IPAddress.TryParse(text, out address))
            return $"'{text}' is not a valid IP address.";

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv4MappedToIPv6)
            return $"'{text}' is not an IPv6 prefix.";

        if (address.IsIPv6Multicast)
            return $"'{text}' is a multicast address and cannot be advertised as a prefix.";

        if (!allowLinkLocal && address.IsIPv6LinkLocal)
            return $"'{text}' is a link-local prefix, which must not be advertised.";

        return null;
    }

    /// <summary>
    ///     This method clears every bit of an IPv6 address after the first
    ///     <paramref name="length" /> bits.
    /// </summary>
    private static IPAddress Mask(IPAddress address, int length)
    {
        byte[] bytes = address.GetAddressBytes();

        for (int index = 0; index < bytes.Length; index++)
        {
            int keptBits = Math.Clamp(length - index * 8, 0, 8);
            bytes[index] &= (byte)(0xFF << (8 - keptBits));
        }

        return new IPAddress(bytes);
    }
}
