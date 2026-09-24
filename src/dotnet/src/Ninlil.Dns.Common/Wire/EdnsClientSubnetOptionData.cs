using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Common.Wire;

/// <summary>
///     This record represents the decoded contents of an EDNS Client Subnet option, defined by
///     RFC 7871: which family the address belongs to, the address itself (already truncated to
///     <see cref="SourcePrefixLength" /> bits, zero-padded beyond that), how much of it the
///     sender is willing to disclose, and how much of it a responding server actually used to
///     tailor its answer.
/// </summary>
/// <param name="Family">The address family the option's own <see cref="Address" /> belongs to.</param>
/// <param name="Address">
///     The (possibly truncated) client subnet address, already masked to
///     <see cref="SourcePrefixLength" /> bits -- see <see cref="EdnsClientSubnetCodec.Encode" />'s
///     own doc comment for why masking happens at encode time rather than being left to a reader.
/// </param>
/// <param name="SourcePrefixLength">
///     How many leading bits of <see cref="Address" /> the sender is disclosing -- RFC 7871 §6's
///     SOURCE PREFIX-LENGTH field. Set by whoever sends the option (a resolver forwarding a
///     query); zero when a query carries no real subnet information at all (an explicit
///     "I have ECS support but decline to disclose a subnet" signal RFC 7871 §7.1.2 permits).
/// </param>
/// <param name="ScopePrefixLength">
///     How many leading bits of <see cref="Address" /> a responding server actually used to
///     tailor its answer -- RFC 7871 §6's SCOPE PREFIX-LENGTH field. Always zero on a query (the
///     querier cannot know this yet); a response's own value tells the querier how much of the
///     address it disclosed was actually needed, which is what makes the answer safe to cache
///     against that whole scope rather than only the exact address queried.
/// </param>
public sealed record EdnsClientSubnetOptionData(
    AddressFamily Family,
    IPAddress Address,
    byte SourcePrefixLength,
    byte ScopePrefixLength
);

/// <summary>
///     This class reads and writes the EDNS Client Subnet option's own wire payload -- the bytes
///     carried inside a <see cref="DnsEdnsOption" /> whose <see cref="DnsEdnsOption.Code" />
///     matches <see cref="Ninlil.Dns.Common.Enum.EdnsOptionCodeEnum.ClientSubnet" />, per RFC 7871
///     §6. Kept alongside <see cref="EdnsClientSubnetOptionData" /> as this project's own single
///     ECS-specific codec, the same "every other EDNS(0) option travels as opaque bytes; a
///     concrete option gets its own dedicated encode/decode pair only once real application logic
///     needs to look inside it" shape RFC 7873 Cookie support already established for itself
///     (see <c>Ninlil.Dns.Server.Core.Cookies.DnsCookieService</c>) -- ECS is the first EDNS
///     option whose *payload* actually needs a reusable, wire-format-aware codec of its own,
///     since (unlike a Cookie, which this server only ever generates and verifies locally) an ECS
///     option's contents need to be read back out of a client's query, and a fresh one built for
///     every outbound upstream query, by more than one caller across more than one project.
/// </summary>
public static class EdnsClientSubnetCodec
{
    /// <summary>This constant contains RFC 7871 §6's assigned family value for IPv4.</summary>
    private const ushort FamilyIPv4 = 1;

    /// <summary>This constant contains RFC 7871 §6's assigned family value for IPv6.</summary>
    private const ushort FamilyIPv6 = 2;

    /// <summary>
    ///     This method builds the raw option payload for an outbound ECS option: a 2-byte family,
    ///     the source and scope prefix lengths, and the address itself truncated to exactly
    ///     <c>ceil(sourcePrefixLength / 8)</c> bytes -- RFC 7871 §6 requires the address field be
    ///     no longer than that, with any bits beyond <paramref name="sourcePrefixLength" /> within
    ///     the final byte zeroed out, so masking happens here, once, rather than trusting every
    ///     caller to have already zeroed the tail bits themselves.
    /// </summary>
    /// <param name="address">The (unmasked) client subnet address to encode.</param>
    /// <param name="sourcePrefixLength">How many leading bits of <paramref name="address" /> to disclose.</param>
    /// <param name="scopePrefixLength">
    ///     How many leading bits a response actually used -- always <c>0</c> on a query, per this
    ///     option's own doc comment.
    /// </param>
    /// <returns>The encoded <see cref="DnsEdnsOption" />, ready to attach to a message's own EDNS options.</returns>
    public static DnsEdnsOption Encode(
        IPAddress address,
        byte sourcePrefixLength,
        byte scopePrefixLength
    )
    {
        ushort family = address.AddressFamily switch
        {
            AddressFamily.InterNetwork => FamilyIPv4,
            AddressFamily.InterNetworkV6 => FamilyIPv6,
            _ => throw new ArgumentException(
                $"'{address}' is neither an IPv4 nor an IPv6 address.",
                nameof(address)
            ),
        };

        byte[] fullAddressBytes = address.GetAddressBytes();
        int addressByteCount = (sourcePrefixLength + 7) / 8;
        byte[] truncatedAddressBytes = new byte[addressByteCount];
        Array.Copy(fullAddressBytes, truncatedAddressBytes, addressByteCount);

        // Zero every bit beyond sourcePrefixLength within the final, partially-disclosed byte --
        // RFC 7871 §6 requires this so two semantically identical /21 addresses that merely
        // differ in their own undisclosed tail bits always encode identically.
        int remainderBits = sourcePrefixLength % 8;

        if (remainderBits != 0 && addressByteCount > 0)
        {
            byte mask = (byte)(0xFF << (8 - remainderBits));
            truncatedAddressBytes[^1] &= mask;
        }

        byte[] payload = new byte[4 + addressByteCount];
        payload[0] = (byte)(family >> 8);
        payload[1] = (byte)family;
        payload[2] = sourcePrefixLength;
        payload[3] = scopePrefixLength;
        Array.Copy(truncatedAddressBytes, 0, payload, 4, addressByteCount);

        return new DnsEdnsOption((ushort)EdnsOptionCodeEnum.ClientSubnet, payload);
    }

    /// <summary>
    ///     This method decodes an ECS option's own raw payload back into
    ///     <see cref="EdnsClientSubnetOptionData" />.
    /// </summary>
    /// <param name="option">
    ///     The option to decode -- this method does not itself check
    ///     <see cref="DnsEdnsOption.Code" />; a caller is expected to have already selected this
    ///     option specifically (see <see cref="Ninlil.Dns.Common.Enum.EdnsOptionCodeEnum.ClientSubnet" />).
    /// </param>
    /// <param name="data">The decoded contents, if decoding succeeded.</param>
    /// <returns>
    ///     A <see langword="bool" /> indicating whether <paramref name="option" />'s own payload
    ///     was a well-formed ECS option -- <see langword="false" /> for anything truncated, an
    ///     unrecognized family, or an address longer than its own family allows, all of which are
    ///     treated as "this option is not usable," never as a reason to fail the whole message
    ///     (RFC 7871 itself has no error-response scheme for a malformed ECS option; the safest
    ///     posture is simply to ignore it, exactly as an unrecognized option code already is).
    /// </returns>
    public static bool TryDecode(DnsEdnsOption option, out EdnsClientSubnetOptionData data)
    {
        data = null;
        byte[] payload = option?.Data;

        // The 2-byte family plus the two 1-byte prefix-length fields must be present before any
        // of them can be read at all -- a shorter payload is truncated/malformed, not decodable.
        if (payload is null || payload.Length < 4)
            return false;

        ushort family = (ushort)((payload[0] << 8) | payload[1]);
        byte sourcePrefixLength = payload[2];
        byte scopePrefixLength = payload[3];

        // Map the wire family value to its .NET AddressFamily and the address's own full bit
        // width, which the length checks below both need; an unrecognized family maps to
        // AddressFamily.Unknown/0 so it fails the very next check rather than needing its own.
        (AddressFamily addressFamily, int maxPrefixLength) = family switch
        {
            FamilyIPv4 => (AddressFamily.InterNetwork, 32),
            FamilyIPv6 => (AddressFamily.InterNetworkV6, 128),
            _ => (AddressFamily.Unknown, 0),
        };

        if (addressFamily == AddressFamily.Unknown || sourcePrefixLength > maxPrefixLength)
            return false;

        // The payload must carry exactly the number of address bytes sourcePrefixLength implies
        // -- neither fewer (truncated) nor more (padded beyond what RFC 7871 §6 permits).
        int expectedAddressByteCount = (sourcePrefixLength + 7) / 8;

        if (payload.Length != 4 + expectedAddressByteCount)
            return false;

        // Copy only the disclosed prefix bytes into a full-width, zero-padded address buffer --
        // IPAddress's own constructor requires a complete 4- or 16-byte array, so the undisclosed
        // tail bits are left as the zeros the array already starts with.
        byte[] fullAddressBytes = new byte[maxPrefixLength / 8];
        Array.Copy(payload, 4, fullAddressBytes, 0, expectedAddressByteCount);

        data = new EdnsClientSubnetOptionData(
            addressFamily,
            new IPAddress(fullAddressBytes),
            sourcePrefixLength,
            scopePrefixLength
        );
        return true;
    }
}
