using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Dns64;

/// <summary>
///     This class implements RFC 6147 DNS64: synthesizing an AAAA answer, embedding a real A
///     record's own IPv4 address into <see cref="Dns64Options.SynthesisPrefix" /> per RFC 6052
///     §2.2's algorithm, for a name that has no AAAA record of its own. Kept in <c>Server.Core</c>
///     (storage-free, IANA/RFC-sourced logic only) rather than <c>Server.Recursive</c> itself,
///     mirroring where <see cref="ClientSubnet.EdnsClientSubnetService" /> lives, so a future
///     consumer outside the recursive resolver (a forwarder re-answering a client's own AAAA
///     query, for example) could reuse it without needing a project reference to
///     <c>Server.Recursive</c>.
///     <para>
///         <b>A synthesized AAAA answer must never be presented as DNSSEC-authenticated.</b> The
///         address this service invents was never actually signed by anyone -- it has no real
///         RRSIG covering it, only whatever RRSIG (if any) covered the real, unrelated A record it
///         was derived from. RFC 6147 §3 explicitly warns that DNS64 synthesis can break DNSSEC
///         validation for the synthesized RRset, and RFC 6840 §5.1 only ever permits the AD bit
///         when *every* RRset in a response was cryptographically validated end-to-end as the
///         literal data returned. A caller that uses this service's own synthesized records in a
///         response is responsible for making sure that response's own <c>AuthenticData</c> bit
///         is never set to <see langword="true" /> -- see <c>RecursiveQueryResolver</c>'s own
///         handling for the one real caller that does this today.
///     </para>
/// </summary>
/// <param name="options">
///     This service's own <see cref="Dns64Options.Enable" /> gate and
///     <see cref="Dns64Options.SynthesisPrefix" /> -- read live via
///     <see cref="IOptionsMonitor{TOptions}" /> so a <c>Dns64SettingsController.Patch</c> write
///     reaches this service's very next synthesis attempt with no restart, the same reasoning
///     every other <c>IOptionsMonitor</c>-bound singleton in this project's query path already
///     documents for itself.
/// </param>
public sealed class Dns64SynthesisService(IOptionsMonitor<Dns64Options> options)
{
    /// <summary>
    ///     This property contains the six prefix lengths RFC 6052 §2.2 defines for embedding a
    ///     32-bit IPv4 address into a 128-bit IPv6 address -- any other length is rejected.
    /// </summary>
    private static readonly int[] ValidPrefixLengths = [32, 40, 48, 56, 64, 96];

    /// <summary>
    ///     This property reports whether DNS64 synthesis is currently enabled at all, read live.
    /// </summary>
    public bool IsEnabled => options.CurrentValue.Enable;

    /// <summary>
    ///     This method builds the synthesized AAAA answer for a name that has no AAAA record but
    ///     does have the A answer given by <paramref name="aAnswer" /> (that sequence's own exact
    ///     shape, including any CNAME hops already followed to reach the real A record(s), the
    ///     same shape <c>IterativeResolutionResult.Answer</c> already carries for a successful A
    ///     resolution). Returns an empty list -- meaning "do not synthesize anything" -- when
    ///     synthesis is disabled, <paramref name="aAnswer" /> is empty, or the currently configured
    ///     <see cref="Dns64Options.SynthesisPrefix" /> is not a legal RFC 6052 §2.2 prefix; a
    ///     misconfigured prefix fails safe (no synthesized answer at all) rather than throwing
    ///     mid-query.
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
                // A CNAME hop already followed on the way to the real A record is carried
                // through unchanged -- the synthesized AAAA name still needs to chain through
                // exactly the same aliases a client resolving the real A record would have seen.
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
                    // RFC 6147 §6.1: a synthesized RRset's own TTL should be no greater than the
                    // real A record's own TTL, so it never outlives the data it was derived from.
                    record.TimeToLive,
                    DnsRecordDataCodecRegistry.Encode(DnsResourceRecordTypeEnum.Aaaa, aaaaData)
                )
            );
        }

        return synthesized;
    }

    /// <summary>
    ///     This method embeds a 32-bit IPv4 address into a 128-bit IPv6 address under a given
    ///     RFC 6052 §2.2 prefix, per that section's own exact bit layout: the prefix occupies the
    ///     leading <paramref name="prefixLength" /> bits; the reserved "u" octet (bits 64-71,
    ///     always zero) appears immediately after the prefix for every length except
    ///     <c>/96</c> (where the prefix itself already fills bits 0-95, leaving no room or need
    ///     for one); the IPv4 address's own 32 bits are split around that "u" octet for every
    ///     prefix length shorter than <c>/64</c>, and placed contiguously right after it for
    ///     <c>/64</c> and <c>/96</c>. All six legal lengths are exact octet boundaries, so no
    ///     partial-byte bit manipulation is ever needed.
    /// </summary>
    /// <param name="ipv4Address">The real IPv4 address being embedded.</param>
    /// <param name="prefixBytes">
    ///     The 16-byte NAT64 prefix network address, already masked so every bit beyond
    ///     <paramref name="prefixLength" /> is zero (see <see cref="TryParsePrefix" />).
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
    ///     This method parses and validates a DNS64 prefix in CIDR notation, returning its own
    ///     16-byte network address (masked so every bit beyond <paramref name="prefixLength" /> is
    ///     zero, regardless of whether the operator-supplied text already was) and prefix length.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when <paramref name="cidr" /> is a well-formed IPv6 CIDR whose
    ///     own prefix length is one of the six RFC 6052 §2.2 defines; <see langword="false" />
    ///     otherwise, with both <see langword="out" /> parameters set to their default.
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

        // Every one of the six legal RFC 6052 prefix lengths is an exact multiple of 8, so
        // masking is always a whole-byte copy -- no partial-byte bit clearing is ever needed.
        Array.Copy(raw, masked, length / 8);

        prefixBytes = masked;
        prefixLength = length;
        return true;
    }
}
