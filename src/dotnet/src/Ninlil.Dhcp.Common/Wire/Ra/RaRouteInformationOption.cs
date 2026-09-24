using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dhcp.Common.Enum;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Wire.Ra;

/// <summary>
///     This class represents RFC 4191 Section 2.3's Route Information option: one specific route
///     this router advertises -- a prefix, a preference relative to any other route or default
///     router on the link, and how long the route stays valid -- distinct from
///     <see cref="RaPrefixInformationOption" />'s own on-link/SLAAC purpose. A host that
///     understands this option uses it for genuine route selection among several routers/routes
///     on the same link; a host that does not understand it simply ignores it, per RFC 4191
///     Section 1's own backward-compatibility design.
///     <para>
///         <b>This option's own value has a genuinely variable length</b>, unlike
///         <see cref="RaPrefixInformationOption" />'s fixed 30 bytes: RFC 4191 Section 2.3 defines
///         exactly three legal shapes, selected by how many significant bits
///         <see cref="PrefixLength" /> actually has --
///         <list type="bullet">
///             <item>
///                 <see cref="PrefixLength" /> is <c>0</c>: no <see cref="Prefix" /> octets are
///                 present at all (a "default route preference only" entry, functionally
///                 equivalent to <c>::/0</c>);
///             </item>
///             <item>
///                 <see cref="PrefixLength" /> is <c>1</c>-<c>64</c>: exactly 8 octets of
///                 <see cref="Prefix" /> are present;
///             </item>
///             <item>
///                 <see cref="PrefixLength" /> is <c>65</c>-<c>128</c>: exactly 16 octets of
///                 <see cref="Prefix" /> are present.
///             </item>
///         </list>
///         <see cref="Encode" /> always chooses the smallest of these three shapes a given
///         <see cref="PrefixLength" /> allows, per RFC 4191 Section 2.3's own stated convention;
///         <see cref="TryDecode" /> rejects a value whose own byte length does not exactly match
///         one of the three, or whose <see cref="PrefixLength" /> does not fall within the range
///         that specific shape implies, as a genuine decode failure rather than silently accepting
///         a non-conformant combination.
///     </para>
/// </summary>
public class RaRouteInformationOption
{
    /// <summary>
    ///     This constant contains the fixed portion of this option's own value that precedes its
    ///     variable-length prefix -- a 1-byte prefix length, a 1-byte packed
    ///     reserved/preference/reserved field, and a 4-byte route lifetime.
    /// </summary>
    private const int FixedHeaderLength = 6;

    /// <summary>
    ///     This constant contains RFC 4191 Section 2.3's own bit position (counted from the
    ///     field's least-significant bit) the two-bit preference value is packed at, within this
    ///     option's own second value byte -- three reserved bits above it, three reserved bits
    ///     below it.
    /// </summary>
    private const int PreferenceBitOffset = 3;

    /// <summary>
    ///     This constant contains the two-bit mask isolating a preference value once it has been
    ///     shifted down to bit position zero.
    /// </summary>
    private const byte PreferenceMask = 0b0000_0011;

    /// <summary>
    ///     This property is RFC 4861 Section 4.6.2's own sentinel value, reused here since RFC
    ///     4191 Section 2.3 gives this option's own Route Lifetime field the identical "all-ones
    ///     means infinity" convention every other Neighbor Discovery lifetime field in this
    ///     project's own codec already follows.
    /// </summary>
    public static readonly TimeSpan InfiniteLifetime = TimeSpan.FromSeconds(uint.MaxValue);

    /// <summary>
    ///     This property contains the number of leading significant bits of <see cref="Prefix" />
    ///     -- <c>0</c> through <c>128</c>. A value of <c>0</c> means this option carries no real
    ///     prefix at all, advertising only a route preference for the link's own default route.
    /// </summary>
    public byte PrefixLength { get; set; }

    /// <summary>
    ///     This property contains this route's own preference relative to any other route (or
    ///     default router) known on the same link -- RFC 4191 Section 2.1's own signed two-bit
    ///     field.
    /// </summary>
    public RaRoutePreferenceEnum Preference { get; set; } = RaRoutePreferenceEnum.Medium;

    /// <summary>
    ///     This property contains how long, from the moment this option was sent, the route
    ///     described by <see cref="Prefix" />/<see cref="PrefixLength" /> remains valid -- RFC
    ///     4191 Section 2.3's own "Route Lifetime" field, a 32-bit number of seconds on the wire,
    ///     or <see cref="InfiniteLifetime" /> for "never expires."
    /// </summary>
    public TimeSpan RouteLifetime { get; set; }

    /// <summary>
    ///     This property contains the advertised route's own prefix, as a full 128-bit address
    ///     whose own leading <see cref="PrefixLength" /> bits are significant --
    ///     <see cref="IPAddress.IPv6Any" /> (<c>::</c>) when <see cref="PrefixLength" /> is
    ///     <c>0</c>, since no real prefix octets exist on the wire in that case at all.
    /// </summary>
    public IPAddress Prefix { get; set; } = IPAddress.IPv6Any;

    /// <summary>
    ///     This method decodes a Route Information option's own value bytes (everything after its
    ///     own type/length header).
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
                + $"prefix length of {prefixLength} -- RFC 4191 Section 2.3 requires 0 octets "
                + "for length 0, 8 octets for lengths 1-64, and 16 octets for lengths 65-128.";
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

        // Every prefix octet beyond what this option's own value actually carries is implicitly
        // zero -- the same "reconstruct a full 128-bit address from a shorter significant prefix"
        // shape RFC 4191 Section 2.3 itself describes.
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
    ///     This method encodes this option's own value bytes (everything after its own
    ///     type/length header, which the caller -- <see cref="RaMessageOptionExtensions" /> --
    ///     adds separately), always choosing the smallest of RFC 4191 Section 2.3's own three
    ///     length variants that <see cref="PrefixLength" /> allows.
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
    ///     This method reports whether a prefix length falls within the specific range RFC 4191
    ///     Section 2.3 requires for a given number of on-wire prefix octets.
    /// </summary>
    private static bool IsPrefixLengthConsistentWithItsOwnOctetCount(
        byte prefixLength,
        int prefixOctets
    ) =>
        prefixOctets switch
        {
            0 => prefixLength == 0,
            8 => prefixLength is >= 1 and <= 64,
            16 => prefixLength is >= 65 and <= 128,
            _ => false,
        };

    /// <summary>
    ///     This method maps a raw two-bit wire value onto its own <see cref="RaRoutePreferenceEnum" />
    ///     member, rejecting RFC 4191 Section 2.1's own reserved binary <c>10</c> pattern.
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
    ///     This method converts a lifetime to the unsigned 32-bit number of seconds RFC 4191
    ///     writes it as, clamping to <see cref="uint.MaxValue" /> (this option's own "infinite"
    ///     sentinel) rather than throwing on an out-of-range value, mirroring
    ///     <see cref="RaPrefixInformationOption" />'s own identical helper.
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
