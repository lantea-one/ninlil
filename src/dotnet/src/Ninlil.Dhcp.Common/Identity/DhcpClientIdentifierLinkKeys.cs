using System.Buffers.Binary;

// Define our namespace.
namespace Ninlil.Dhcp.Common.Identity;

/// <summary>
///     This static class derives the hardware-level facts a stored DHCP client identifier embeds,
///     so a DHCPv4 identity and a DHCPv6 identity belonging to the same physical device can be
///     recognized as one. A dual-stack device presents two unrelated-looking identifiers -- a
///     MAC-address-based one to DHCPv4 and a DHCP Unique Identifier (DUID) to DHCPv6 -- but very
///     often one is recoverable from the other:
///     <list type="bullet">
///         <item>
///             A DUID-LLT (type 1, RFC 8415 Section 11.2) or DUID-LL (type 3, RFC 8415
///             Section 11.4) carries a hardware type and a link-layer address; with hardware type 1
///             (Ethernet, IANA's ARP hardware-type registry) and a 6-octet address, that address is
///             the device's own MAC.
///         </item>
///         <item>
///             A DHCPv4 identifier is either a raw 6-octet <c>chaddr</c> (a MAC), an RFC 2132
///             Section 9.14 Client-identifier of hardware type 1 followed by a 6-octet MAC, or an
///             RFC 4361 Section 6.1 Client-identifier -- type 255, a 4-octet IAID, then the very
///             same DUID the client uses for DHCPv6, which links the two even when that DUID
///             embeds no MAC at all (a DUID-EN or DUID-UUID).
///         </item>
///     </list>
///     Each fact becomes one opaque "link key" string (<c>mac:</c> or <c>duid:</c> followed by
///     lowercase hexadecimal); two identifiers are the same device when their key sets intersect.
///     <para>
///         Input is the colon-separated lowercase hexadecimal form DHCP leases store identifiers
///         in, though any separator and case are tolerated. An identifier this class cannot
///         interpret simply yields no keys -- never a guess -- so an unrecognized shape can only
///         ever cause a missed merge, never a false one.
///     </para>
/// </summary>
public static class DhcpClientIdentifierLinkKeys
{
    /// <summary>The IANA ARP hardware type for Ethernet (10Mb), used by every Ethernet/Wi-Fi MAC.</summary>
    private const ushort EthernetHardwareType = 1;

    /// <summary>The length, in octets, of an Ethernet MAC address.</summary>
    private const int MacAddressLength = 6;

    /// <summary>
    ///     This method derives every link key a DHCPv4 client identifier embeds.
    /// </summary>
    /// <param name="identifier">The stored DHCPv4 client identifier.</param>
    /// <returns>The identifier's link keys; empty when nothing can be derived.</returns>
    public static IReadOnlySet<string> FromDhcpv4Identifier(string identifier)
    {
        HashSet<string> keys = [];
        byte[] bytes = ParseHex(identifier);
        if (bytes is null)
            return keys;

        // A plain chaddr: the MAC itself.
        if (bytes.Length == MacAddressLength)
            keys.Add(MacKey(bytes));
        // RFC 2132 Section 9.14: hardware type 1 followed by the MAC.
        else if (bytes.Length == 1 + MacAddressLength && bytes[0] == EthernetHardwareType)
            keys.Add(MacKey(bytes.AsSpan(1)));
        // RFC 4361 Section 6.1: type 255, a 4-octet IAID, then the client's own DUID.
        else if (bytes.Length > 5 && bytes[0] == 0xFF)
            AddDuidKeys(bytes.AsSpan(5), keys);

        return keys;
    }

    /// <summary>
    ///     This method derives every link key a DHCPv6 client identifier (a DUID) embeds.
    /// </summary>
    /// <param name="identifier">The stored DHCPv6 client identifier.</param>
    /// <returns>The identifier's link keys; empty when nothing can be derived.</returns>
    public static IReadOnlySet<string> FromDhcpv6Identifier(string identifier)
    {
        HashSet<string> keys = [];
        byte[] bytes = ParseHex(identifier);
        if (bytes is not null)
            AddDuidKeys(bytes, keys);
        return keys;
    }

    /// <summary>
    ///     This method adds a DUID's own key, plus its embedded MAC's key when it carries one.
    /// </summary>
    private static void AddDuidKeys(ReadOnlySpan<byte> duid, HashSet<string> keys)
    {
        // Every DUID begins with a 2-octet type code (RFC 8415 Section 11.1); anything shorter,
        // or with nothing after the type, is not a real DUID.
        if (duid.Length <= 2)
            return;

        keys.Add("duid:" + Convert.ToHexStringLower(duid));

        ushort duidType = BinaryPrimitives.ReadUInt16BigEndian(duid);

        // Locate the hardware type and link-layer address for the two link-layer-based types:
        // DUID-LLT is type(2) + hardware type(2) + time(4) + address; DUID-LL omits the time.
        int addressOffset = duidType switch
        {
            1 => 8,
            3 => 4,
            _ => -1,
        };
        if (addressOffset < 0 || duid.Length != addressOffset + MacAddressLength)
            return;

        if (BinaryPrimitives.ReadUInt16BigEndian(duid[2..]) == EthernetHardwareType)
            keys.Add(MacKey(duid[addressOffset..]));
    }

    /// <summary>
    ///     This method formats a MAC address as its link key.
    /// </summary>
    private static string MacKey(ReadOnlySpan<byte> mac) => "mac:" + Convert.ToHexStringLower(mac);

    /// <summary>
    ///     This method parses a hexadecimal identifier string, ignoring the usual <c>:</c>/<c>-</c>/
    ///     <c>.</c> separators, or returns <see langword="null" /> when it contains anything else
    ///     (an operator-typed free-form identifier is never reinterpreted as hardware data), no
    ///     hex digits at all, or an odd number of them.
    /// </summary>
    private static byte[] ParseHex(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            return null;

        if (identifier.Any(c => !Uri.IsHexDigit(c) && c is not (':' or '-' or '.')))
            return null;

        string digits = new(identifier.Where(Uri.IsHexDigit).ToArray());
        if (digits.Length == 0 || digits.Length % 2 != 0)
            return null;

        return Convert.FromHexString(digits);
    }
}
