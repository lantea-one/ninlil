using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 4191 Section 2.3's Route Information option: one route this
///     router advertises, with a prefix, a preference relative to other routes and default
///     routers on the link, and a lifetime. It is separate from the on-link and SLAAC role of
///     <see cref="RaPrefixInformationOption" />. Hosts that understand it use it to choose between
///     routers; others ignore it.
///     <para>
///         <b>The value length varies</b>, unlike the fixed 30 bytes of
///         <see cref="RaPrefixInformationOption" />. The prefix field holds 0, 8 or 16 octets.
///         <see cref="Encode" /> always writes the smallest form <see cref="PrefixLength" />
///         allows:
///         <list type="bullet">
///             <item>
///                 <see cref="PrefixLength" /> <c>0</c>: no prefix octets (a preference for the
///                 default route, equivalent to <c>::/0</c>);
///             </item>
///             <item><see cref="PrefixLength" /> <c>1</c>-<c>64</c>: 8 prefix octets;</item>
///             <item><see cref="PrefixLength" /> <c>65</c>-<c>128</c>: 16 prefix octets.</item>
///         </list>
///         <see cref="TryDecode" /> also accepts a longer prefix field than needed, as RFC 4191
///         Section 2.3 allows: 8 or 16 octets for length 0, and 16 octets for lengths 1-64. The
///         option's Length is 1, 2 or 3 (0, 8 or 16 prefix octets), and the prefix field must be
///         long enough to hold <see cref="PrefixLength" /> bits.
///     </para>
/// </summary>
public class RaRouteInformationOption
{
    /// <summary>
    ///     This constant contains the length of the fixed part of the value before the prefix: a
    ///     1-byte prefix length, a 1-byte field holding the preference between reserved bits, and
    ///     a 4-byte route lifetime.
    /// </summary>
    private const int FixedHeaderLength = 6;

    /// <summary>
    ///     This constant contains the bit position (from the least-significant bit) of the
    ///     two-bit preference in the second value byte, with three reserved bits on each side
    ///     (RFC 4191 Section 2.3).
    /// </summary>
    private const int PreferenceBitOffset = 3;

    /// <summary>
    ///     This constant contains the two-bit mask for the preference after shifting it to bit 0.
    /// </summary>
    private const byte PreferenceMask = 0b0000_0011;

    /// <summary>
    ///     This field holds the all-ones Route Lifetime, which RFC 4191 Section 2.3 defines as
    ///     infinity, like the RFC 4861 Prefix Information lifetimes.
    /// </summary>
    public static readonly TimeSpan InfiniteLifetime = TimeSpan.FromSeconds(uint.MaxValue);

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />,
    ///     <c>0</c> through <c>128</c>. <c>0</c> means no prefix: the option only sets a
    ///     preference for the default route.
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property contains the route's preference relative to other routes and default
    ///     routers on the link (RFC 4191 Section 2.1's signed two-bit field). The default is
    ///     <see cref="RaRoutePreferenceEnum.Medium" />.
    /// </summary>
    public RaRoutePreferenceEnum Preference { get; set; } = RaRoutePreferenceEnum.Medium;

    /// <summary>
    ///     This property contains how long after this option is sent the route stays valid (RFC
    ///     4191 Section 2.3's "Route Lifetime", 32-bit seconds on the wire), or
    ///     <see cref="InfiniteLifetime" />.
    /// </summary>
    public TimeSpan RouteLifetime { get; set; }

    /// <summary>
    ///     This property contains the route's prefix as a 128-bit address whose leading
    ///     <see cref="PrefixLength" /> bits are significant. It is <see cref="IPAddress.IPv6Any" />
    ///     (<c>::</c>) by default and when <see cref="PrefixLength" /> is <c>0</c>.
    /// </summary>
    public IPAddress Prefix { get; set; } = IPAddress.IPv6Any;

    /// <summary>
    ///     This method decodes a Route Information option's value (everything after the
    ///     type/length header). It rejects a value that is not 6, 14 or 22 bytes long (RFC 4191
    ///     Section 2.3 Length 1, 2 or 3), a prefix field too short for the prefix length (see the
    ///     class summary) and the reserved preference value.
    /// </summary>
    internal static bool TryDecode(
        ReadOnlySpan<byte> data,
        out RaRouteInformationOption option,
        out string failureReason
    )
    {
        option = null;

        if (
            data.Length
            is not (FixedHeaderLength or FixedHeaderLength + 8 or FixedHeaderLength + 16)
        )
        {
            failureReason =
                "A Route Information option's own value must be exactly 6, 14, or 22 bytes long "
                + "(no prefix, an 8-octet prefix, or a 16-octet prefix).";
            return false;
        }

        byte prefixLength = data[0];
        int prefixOctets = data.Length - FixedHeaderLength;

        if (!IsPrefixLengthConsistentWithItsOwnOctetCount(prefixLength, prefixOctets))
        {
            failureReason =
                $"A Route Information option's own {prefixOctets}-octet prefix cannot carry a "
                + $"prefix length of {prefixLength}: RFC 4191 Section 2.3 needs at least 8 octets "
                + "for lengths 1-64 and 16 octets for lengths 65-128.";
            return false;
        }

        byte rawPreference = (byte)((data[1] >> PreferenceBitOffset) & PreferenceMask);

        if (!TryMapRawPreference(rawPreference, out RaRoutePreferenceEnum preference))
        {
            failureReason =
                "A Route Information option used RFC 4191 Section 2.1's own reserved preference "
                + "value (binary 10), which must never be sent.";
            return false;
        }

        uint lifetimeSeconds = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(2, 4));

        // Prefix octets not carried on the wire are zero (RFC 4191 Section 2.3).
        Span<byte> fullPrefix = stackalloc byte[16];
        if (prefixOctets > 0)
            data.Slice(FixedHeaderLength, prefixOctets).CopyTo(fullPrefix);

        option = new RaRouteInformationOption
        {
            PrefixLength = prefixLength,
            Preference = preference,
            RouteLifetime = TimeSpan.FromSeconds(lifetimeSeconds),
            Prefix = new IPAddress(fullPrefix),
        };
        failureReason = null;
        return true;
    }

    /// <summary>
    ///     This method encodes this option's value (everything after the type/length header,
    ///     which <see cref="RaMessageOptionExtensions" /> adds), using the smallest prefix field
    ///     <see cref="PrefixLength" /> allows.
    /// </summary>
    internal byte[] Encode()
    {
        if (PrefixLength > 128)
            throw new ArgumentOutOfRangeException(
                nameof(PrefixLength),
                PrefixLength,
                "A Route Information option's own prefix length must be between 0 and 128."
            );

        int prefixOctets = PrefixLength switch
        {
            0 => 0,
            <= 64 => 8,
            _ => 16,
        };

        if (prefixOctets > 0 && Prefix is not { AddressFamily: AddressFamily.InterNetworkV6 })
            throw new ArgumentException(
                "A Route Information option's prefix must be an IPv6 address.",
                nameof(Prefix)
            );

        byte[] result = new byte[FixedHeaderLength + prefixOctets];
        result[0] = PrefixLength;
        result[1] = (byte)(((byte)Preference & PreferenceMask) << PreferenceBitOffset);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(2, 4), ToWireSeconds(RouteLifetime));

        if (prefixOctets > 0)
        {
            Span<byte> fullPrefix = stackalloc byte[16];
            Prefix.TryWriteBytes(fullPrefix, out _);
            fullPrefix[..prefixOctets].CopyTo(result.AsSpan(FixedHeaderLength, prefixOctets));
        }

        return result;
    }

    /// <summary>
    ///     This method reports whether a prefix field of the given size can hold a prefix length
    ///     (RFC 4191 Section 2.3): 0 octets only for length 0, 8 octets for 0-64 and 16 octets for
    ///     0-128. A field longer than the smallest form is valid.
    /// </summary>
    private static bool IsPrefixLengthConsistentWithItsOwnOctetCount(
        byte prefixLength,
        int prefixOctets
    ) =>
        prefixOctets switch
        {
            0 => prefixLength == 0,
            8 => prefixLength <= 64,
            16 => prefixLength <= 128,
            _ => false,
        };

    /// <summary>
    ///     This method maps a raw two-bit value to its <see cref="RaRoutePreferenceEnum" /> member,
    ///     rejecting the reserved binary <c>10</c> pattern (RFC 4191 Section 2.1).
    /// </summary>
    private static bool TryMapRawPreference(
        byte rawPreference,
        out RaRoutePreferenceEnum preference
    )
    {
        switch (rawPreference)
        {
            case (byte)RaRoutePreferenceEnum.Medium:
                preference = RaRoutePreferenceEnum.Medium;
                return true;
            case (byte)RaRoutePreferenceEnum.High:
                preference = RaRoutePreferenceEnum.High;
                return true;
            case (byte)RaRoutePreferenceEnum.Low:
                preference = RaRoutePreferenceEnum.Low;
                return true;
            default:
                preference = default;
                return false;
        }
    }

    /// <summary>
    ///     This method converts a lifetime to unsigned 32-bit seconds, writing a value at or above
    ///     <see cref="uint.MaxValue" /> seconds as infinity, like the helper in
    ///     <see cref="RaPrefixInformationOption" />. A negative value throws.
    /// </summary>
    private static uint ToWireSeconds(TimeSpan lifetime)
    {
        if (lifetime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(
                nameof(lifetime),
                lifetime,
                "A Route Information option's own Route Lifetime cannot be negative."
            );

        double totalSeconds = lifetime.TotalSeconds;
        return totalSeconds >= uint.MaxValue ? uint.MaxValue : (uint)totalSeconds;
    }
}
