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
///     <see cref="ClientSubnet.EdnsClientSubnetService" />. It does not skip non-global IPv4
///     addresses.
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
    ///     This property reports whether DNS64 synthesis is enabled.
    /// </summary>
    public bool IsEnabled => options.CurrentValue.Enable;

    /// <summary>
    ///     This method builds the synthesized AAAA answer from the A answer
    ///     <paramref name="aAnswer" />, which may include CNAME records followed on the way (the
    ///     shape of <c>IterativeResolutionResult.Answer</c>). Each A record becomes an AAAA record
    ///     and other records are copied. It returns an empty list, meaning "do not synthesize",
    ///     when synthesis is disabled, <paramref name="aAnswer" /> is empty, or
    ///     <see cref="Dns64Options.SynthesisPrefix" /> is not a valid RFC 6052 prefix.
    /// </summary>
    public IReadOnlyList<DnsResourceRecord> SynthesizeAaaaRecords(
        IReadOnlyList<DnsResourceRecord> aAnswer
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

        List<DnsResourceRecord> synthesized = [];

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

            byte[] ipv6Bytes = EmbedIPv4Address(aData.Address, prefixBytes, prefixLength);
            AaaaRecordData aaaaData = new(new IPAddress(ipv6Bytes));

            synthesized.Add(
                new DnsResourceRecord(
                    record.Name,
                    DnsResourceRecordTypeEnum.Aaaa,
                    record.Class,
                    // RFC 6147 §5.1.7: the synthesized TTL should not exceed the A record's TTL.
                    record.TimeToLive,
                    DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Aaaa, aaaaData)
                )
            );
        }

        return synthesized;
    }

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
    ///     RFC 6052 §2.2 lengths; otherwise <see langword="false" />, with both
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

        prefixBytes = masked;
        prefixLength = length;
        return true;
    }
}
