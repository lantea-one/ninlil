using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class computes and formats the key the DHCPv4 engine uses to recognize a
///     client, the value stored in <see cref="Data.Abstractions.Entity.LeaseEntity.ClientIdentifier" />
///     and looked up in <see cref="Data.Abstractions.Entity.DhcpReservationEntity.ClientIdentifier" />.
///     <para>
///         RFC 2131 Section 4.2 identifies a client by its Client-identifier option (tag 61) when
///         it sends one, and by its hardware address (<c>chaddr</c>) otherwise, which is the
///         usual case since most devices send no Client-identifier. The key is the raw option
///         bytes when present, otherwise the <c>chaddr</c> bytes alone, without the hardware-type
///         octet RFC 2132 Section 9.14 puts in front of a Client-identifier. That lets an operator
///         reserve by plain MAC address, as a packet capture or router client list shows it.
///     </para>
///     <para>
///         The key is stored as colon-separated lowercase hex (<c>aa:bb:cc:dd:ee:ff</c>).
///         Reservations are found by exact string match on that form (see
///         <see cref="Data.Abstractions.Repository.IDhcpReservationRepository.FindByClientIdentifierAsync" />),
///         so a reservation entered in another case or with other separators does not match.
///         <see cref="AreEqual" />, which ignores separators and case, is used only when the
///         allocation services compare a stored lease's key with the request's.
///     </para>
/// </summary>
internal static class Dhcpv4ClientIdentifier
{
    /// <summary>
    ///     This method returns the request's Client-identifier option bytes, or the
    ///     <c>chaddr</c> bytes when it has none.
    /// </summary>
    /// <param name="request">The request to compute an identifier for.</param>
    /// <returns>The effective client identifier's raw bytes.</returns>
    public static byte[] Resolve(Dhcpv4Message request) =>
        request.TryGetClientIdentifier(out byte[] clientIdentifierOption)
            ? clientIdentifierOption
            : request.ClientHardwareAddress;

    /// <summary>
    ///     This method formats the request's client key (see <see cref="Resolve" />) as
    ///     colon-separated lowercase hex.
    /// </summary>
    /// <param name="request">The request to format an identifier for.</param>
    /// <returns>The formatted client identifier string.</returns>
    public static string Format(Dhcpv4Message request) => Format(Resolve(request));

    /// <summary>
    ///     This method formats identifier bytes as colon-separated lowercase hex, the stored form.
    /// </summary>
    /// <param name="identifier">The raw identifier bytes to format.</param>
    /// <returns>The formatted client identifier string.</returns>
    public static string Format(byte[] identifier) =>
        string.Join(':', identifier.Select(b => b.ToString("x2")));

    /// <summary>
    ///     This method reports whether two client identifier strings have the same hex digits,
    ///     ignoring separators and case. Empty or whitespace strings never match.
    /// </summary>
    /// <param name="first">The first client identifier string to compare.</param>
    /// <param name="second">The second client identifier string to compare.</param>
    /// <returns><see langword="true" /> if both refer to the same client.</returns>
    public static bool AreEqual(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);

    /// <summary>
    ///     This method keeps only the hex digits of an identifier string, lowercased.
    /// </summary>
    private static string Normalize(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());
}
