using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Dns64;

/// <summary>
///     This class implements RFC 6147 DNS64: for a name with no AAAA record, it builds AAAA
///     records by embedding each A record's IPv4 address into
///     <see cref="Dns64Options.SynthesisPrefix" /> (RFC 6052 §2.2). <c>RecursiveQueryResolver</c>
///     is its only caller. It needs no storage, so it lives in <c>Server.Core</c> next to
///     <see cref="ClientSubnet.EdnsClientSubnetService" />. Under the Well-Known Prefix it
///     skips IPv4 addresses that are not globally reachable (RFC 6052 §3.1); a network-specific
///     prefix embeds every address.
///     <para>
///         <b>A synthesized answer must never carry the AD bit.</b> No one signed the invented
///         addresses; any RRSIG covered only the A record they came from. RFC 6147 §3 warns that
///         synthesis breaks DNSSEC validation of the synthesized RRset. The caller must leave
///         <c>AuthenticData</c> unset on a response that contains these records, as
///         <c>RecursiveQueryResolver</c> does.
///     </para>
/// </summary>
/// <param name="options">
///     The DNS64 settings, read through <see cref="IOptionsMonitor{TOptions}" /> on each call so
///     a change made through <c>Dns64SettingsController</c> applies without a restart.
/// </param>
public sealed class Dns64SynthesisService(IOptionsMonitor<Dns64Options> options)
{
    /// <summary>
    ///     This field contains the six prefix lengths RFC 6052 §2.2 defines; any other length is
    ///     rejected.
    /// </summary>
    private static readonly int[] ValidPrefixLengths = [32, 40, 48, 56, 64, 96];

    /// <summary>
    ///     This field contains the RFC 6052 §2.1 Well-Known Prefix <c>64:ff9b::/96</c>, as the
    ///     16 bytes <see cref="TryParsePrefix" /> returns for it.
    /// </summary>
    private static readonly byte[] WellKnownPrefixBytes = IPAddress
        .Parse("64:ff9b::")
        .GetAddressBytes();

    /// <summary>
    ///     This field lists the IPv4 blocks that are not globally reachable, as the IANA IPv4
    ///     Special-Purpose Address Registry marks them ("Globally Reachable" = False), plus
    ///     multicast. <see cref="IsGloballyReachableIPv4" /> checks the two globally reachable
    ///     holes in <c>192.0.0.0/24</c> first. The registry's finer rows inside these blocks
    ///     (0.0.0.0/32, 192.0.0.0/29, 192.0.0.8/32, 192.0.0.170/32, 192.0.0.171/32 and
    ///     192.88.99.2/32) need no row of their own.
    /// </summary>
    private static readonly (uint Network, int Bits)[] NonGlobalIPv4Blocks =
    [
        // RFC 791 §3.2 "this network" (RFC 1122 §3.2.1.3).
        (ToUInt32(0, 0, 0, 0), 8),
        // RFC 1918 private use.
        (ToUInt32(10, 0, 0, 0), 8),
        // RFC 6598 shared address space.
        (ToUInt32(100, 64, 0, 0), 10),
        // RFC 1122 §3.2.1.3 loopback.
        (ToUInt32(127, 0, 0, 0), 8),
        // RFC 3927 link local.
        (ToUInt32(169, 254, 0, 0), 16),
        // RFC 1918 private use.
        (ToUInt32(172, 16, 0, 0), 12),
        // RFC 6890 IETF protocol assignments; 192.0.0.9 and 192.0.0.10 are carved out.
        (ToUInt32(192, 0, 0, 0), 24),
        // RFC 5737 documentation (TEST-NET-1).
        (ToUInt32(192, 0, 2, 0), 24),
        // RFC 7526 deprecated 6to4 relay anycast (the registry leaves the column blank).
        (ToUInt32(192, 88, 99, 0), 24),
        // RFC 1918 private use.
        (ToUInt32(192, 168, 0, 0), 16),
        // RFC 2544 benchmarking.
        (ToUInt32(198, 18, 0, 0), 15),
        // RFC 5737 documentation (TEST-NET-2).
        (ToUInt32(198, 51, 100, 0), 24),
        // RFC 5737 documentation (TEST-NET-3).
        (ToUInt32(203, 0, 113, 0), 24),
        // RFC 5771 multicast; not in the registry, never a host address.
        (ToUInt32(224, 0, 0, 0), 4),
        // RFC 1112 §4 reserved.
        (ToUInt32(240, 0, 0, 0), 4),
        // RFC 919 limited broadcast.
        (ToUInt32(255, 255, 255, 255), 32),
    ];

    /// <summary>
    ///     This constant contains the TTL ceiling, in seconds, when the negative AAAA answer
    ///     carried no SOA record (RFC 6147 §5.1.7: "the TTL of the original A RR or 600 seconds,
    ///     whichever is shorter").
    /// </summary>
    private const uint NoSoaTimeToLiveSeconds = 600;

    /// <summary>
    ///     This property reports whether DNS64 synthesis is enabled.
    /// </summary>
    public bool IsEnabled => options.CurrentValue.Enable;

    /// <summary>
    ///     This method builds the synthesized AAAA answer from the A answer
    ///     <paramref name="aAnswer" />, which may include CNAME records followed on the way (the
    ///     shape of <c>IterativeResolutionResult.Answer</c>). Each A record becomes an AAAA record
    ///     and other records are copied. It returns an empty list, meaning "do not synthesize",
    ///     when synthesis is disabled, <paramref name="aAnswer" /> is empty, or
    ///     <see cref="Dns64Options.SynthesisPrefix" /> is not a valid RFC 6052 prefix. Under the
    ///     Well-Known Prefix an A record whose address is not globally reachable
    ///     (<see cref="IsGloballyReachableIPv4" />) is skipped, as RFC 6052 §3.1 requires.
    ///     <para>
    ///         The TTL of each synthesized record is the smaller of the A record's TTL and the TTL
    ///         of <paramref name="negativeAnswerSoa" />, the SOA record of the negative AAAA answer
    ///         (RFC 6147 §5.1.7). Without that SOA the smaller of the A record's TTL and 600
    ///         seconds is used, as the same section says to.
    ///     </para>
    /// </summary>
    /// <param name="aAnswer">The A answer, with any CNAME records followed on the way.</param>
    /// <param name="negativeAnswerSoa">
    ///     The SOA record from the NoData answer to the AAAA query, or <see langword="null" />
    ///     when that answer carried none.
    /// </param>
    public IReadOnlyList<DnsResourceRecord> SynthesizeAaaaRecords(
        IReadOnlyList<DnsResourceRecord> aAnswer,
        DnsResourceRecord negativeAnswerSoa = null
    )
    {
        if (!IsEnabled || aAnswer.Count == 0)
            return [];

        if (
            !TryParsePrefix(
                options.CurrentValue.SynthesisPrefix,
                out byte[] prefixBytes,
                out int prefixLength
            )
        )
            return [];

        bool wellKnownPrefix =
            prefixLength == 96 && prefixBytes.AsSpan().SequenceEqual(WellKnownPrefixBytes);
        List<DnsResourceRecord> synthesized = [];
        uint ttlCeiling = negativeAnswerSoa?.TimeToLive ?? NoSoaTimeToLiveSeconds;

        foreach (DnsResourceRecord record in aAnswer)
        {
            if (record.Type != DnsResourceRecordTypeEnum.A)
            {
                // Keep CNAME records so the answer chains through the same aliases.
                synthesized.Add(record);
                continue;
            }

            if (
                DnsRecordDataCodecRegistry.Decode(DnsResourceRecordTypeEnum.A, record.Data)
                is not ARecordData aData
            )
                continue;

            // RFC 6052 §3.1: the Well-Known Prefix must not represent non-global IPv4
            // addresses. A network-specific prefix may, because the operator's NAT64 gateway
            // owns it.
            if (wellKnownPrefix && !IsGloballyReachableIPv4(aData.Address))
                continue;

            byte[] ipv6Bytes = EmbedIPv4Address(aData.Address, prefixBytes, prefixLength);
            AaaaRecordData aaaaData = new(new IPAddress(ipv6Bytes));

            synthesized.Add(
                new DnsResourceRecord(
                    record.Name,
                    DnsResourceRecordTypeEnum.Aaaa,
                    record.Class,
                    // RFC 6147 §5.1.7: the smaller of the A record's TTL and the SOA TTL (or
                    // 600 seconds when the negative answer had no SOA), in keeping with negative
                    // caching, so the AAAA record does not outlive the NoData answer.
                    Math.Min(record.TimeToLive, ttlCeiling),
                    DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Aaaa, aaaaData)
                )
            );
        }

        return synthesized;
    }

    /// <summary>
    ///     This method tells whether an AAAA address is inside <c>::ffff:0:0/96</c>, the IPv4-mapped
    ///     range. RFC 6147 §5.1.4 says an IPv6-only client cannot use such an address, so a DNS64
    ///     resolver treats the AAAA record as absent.
    /// </summary>
    public static bool IsExcludedAaaaAddress(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return false;

        byte[] bytes = address.GetAddressBytes();

        for (int index = 0; index < 10; index++)
        {
            if (bytes[index] != 0)
                return false;
        }

        return bytes[10] == 0xff && bytes[11] == 0xff;
    }

    /// <summary>
    ///     This method tells whether an IPv4 address is globally reachable: not in any block
    ///     the IANA IPv4 Special-Purpose Address Registry marks as not globally reachable, and
    ///     not multicast. <c>192.0.0.9</c> (PCP anycast) and <c>192.0.0.10</c> (TURN anycast) are
    ///     globally reachable although the rest of <c>192.0.0.0/24</c> is not. Any other address
    ///     family returns <see langword="false" />.
    /// </summary>
    public static bool IsGloballyReachableIPv4(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = address.GetAddressBytes();
        uint value = ToUInt32(bytes[0], bytes[1], bytes[2], bytes[3]);

        // The registry lists these two rows as globally reachable inside the not-reachable
        // 192.0.0.0/24, so they are checked before the blocks.
        if (value == ToUInt32(192, 0, 0, 9) || value == ToUInt32(192, 0, 0, 10))
            return true;

        foreach ((uint network, int bits) in NonGlobalIPv4Blocks)
        {
            uint mask = uint.MaxValue << (32 - bits);

            if ((value & mask) == network)
                return false;
        }

        return true;
    }

    /// <summary>
    ///     This method packs four octets into a host-order 32-bit address, first octet highest.
    /// </summary>
    private static uint ToUInt32(byte a, byte b, byte c, byte d) =>
        ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;

    /// <summary>
    ///     This method embeds an IPv4 address into an IPv6 address under an RFC 6052 §2.2 prefix.
    ///     The prefix fills the first <paramref name="prefixLength" /> bits. For every length but
    ///     <c>/96</c>, bits 64 to 71 are the reserved "u" octet and stay zero; for lengths below
    ///     <c>/64</c> the IPv4 bits are split around it. All six lengths are whole octets.
    /// </summary>
    /// <param name="ipv4Address">The IPv4 address to embed.</param>
    /// <param name="prefixBytes">
    ///     The 16-byte NAT64 prefix, masked so every bit after <paramref name="prefixLength" /> is
    ///     zero (see <see cref="TryParsePrefix" />).
    /// </param>
    /// <param name="prefixLength">One of the six lengths <see cref="ValidPrefixLengths" /> lists.</param>
    public static byte[] EmbedIPv4Address(
        IPAddress ipv4Address,
        byte[] prefixBytes,
        int prefixLength
    )
    {
        if (ipv4Address.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException(
                $"'{ipv4Address}' is not an IPv4 address.",
                nameof(ipv4Address)
            );

        byte[] v4 = ipv4Address.GetAddressBytes();
        byte[] result = (byte[])prefixBytes.Clone();

        switch (prefixLength)
        {
            case 32:
                Array.Copy(v4, 0, result, 4, 4);
                result[8] = 0;
                break;
            case 40:
                Array.Copy(v4, 0, result, 5, 3);
                result[8] = 0;
                result[9] = v4[3];
                break;
            case 48:
                Array.Copy(v4, 0, result, 6, 2);
                result[8] = 0;
                Array.Copy(v4, 2, result, 9, 2);
                break;
            case 56:
                result[7] = v4[0];
                result[8] = 0;
                Array.Copy(v4, 1, result, 9, 3);
                break;
            case 64:
                result[8] = 0;
                Array.Copy(v4, 0, result, 9, 4);
                break;
            case 96:
                Array.Copy(v4, 0, result, 12, 4);
                break;
            default:
                throw new ArgumentException(
                    $"'/{prefixLength}' is not a legal RFC 6052 §2.2 DNS64 prefix length -- "
                        + "only /32, /40, /48, /56, /64, and /96 are defined.",
                    nameof(prefixLength)
                );
        }

        return result;
    }

    /// <summary>
    ///     This method parses a DNS64 prefix in CIDR notation into its 16-byte network address
    ///     (with every bit after <paramref name="prefixLength" /> cleared) and its length.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="cidr" /> is an IPv6 CIDR with one of the six
    ///     RFC 6052 §2.2 lengths that can carry synthesized addresses: its "u" octet (bits 64 to
    ///     71) is zero, it is not inside the IPv4-mapped range <c>::ffff:0:0/96</c> and it is not
    ///     multicast. Otherwise <see langword="false" />, with both
    ///     <see langword="out" /> parameters set to their defaults.
    /// </returns>
    public static bool TryParsePrefix(string cidr, out byte[] prefixBytes, out int prefixLength)
    {
        prefixBytes = null;
        prefixLength = 0;

        if (string.IsNullOrWhiteSpace(cidr))
            return false;

        string[] parts = cidr.Split('/');

        if (
            parts.Length != 2
            || !IPAddress.TryParse(parts[0], out IPAddress address)
            || address.AddressFamily != AddressFamily.InterNetworkV6
            || !int.TryParse(parts[1], out int length)
            || Array.IndexOf(ValidPrefixLengths, length) < 0
        )
            return false;

        byte[] raw = address.GetAddressBytes();
        byte[] masked = new byte[16];

        // Every valid length is a multiple of 8, so masking is a whole-byte copy.
        Array.Copy(raw, masked, length / 8);

        // RFC 6052 §2.2: bits 64 to 71 are the "u" octet and must be zero; only a /96 prefix can
        // set them. A prefix inside ::ffff:0:0/96 would give IPv4-mapped addresses (RFC 6147
        // §5.1.4 excludes those), and a multicast prefix gives addresses no host can have.
        if (masked[8] != 0 || IsExcludedAaaaAddress(new IPAddress(masked)) || masked[0] == 0xff)
            return false;

        prefixBytes = masked;
        prefixLength = length;
        return true;
    }
}
