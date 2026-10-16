using Ninlil.Dhcp.Common.Wire;
using Ninlil.Dns.Common.Net;

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
///         The key is stored as colon-separated lowercase hex (<c>aa:bb:cc:dd:ee:ff</c>), the
///         normal form of <see cref="ClientIdentifierFormat" />. The web API normalises
///         reservation, client and static-mapping identifiers to that form on write, so
///         reservations are found by exact string match (see
///         <see cref="Data.Abstractions.Repository.IDhcpReservationRepository.FindByClientIdentifierAsync" />).
///         <see cref="AreEqual" /> compares a stored lease's key with the request's.
///     </para>
///     <para>
///         A key is at most <see cref="ClientIdentifierFormat.MaximumBytes" /> bytes. That is the
///         size of the stored identifier columns and of the composite unique indexes over them,
///         which fit the SQL Server and MySQL index key limits only at that size.
///         <see cref="Dhcpv4RequestHandler" /> refuses a longer key before it allocates or saves
///         anything (see <see cref="LongClientIdentifierLimiter" />).
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
    public static string Format(byte[] identifier) => ClientIdentifierFormat.Format(identifier);

    /// <summary>
    ///     This method reports whether two client identifier strings name the same identifier
    ///     under <see cref="ClientIdentifierFormat.AreEqual" />, so separators and case do not
    ///     matter. Identical strings also match. Empty or whitespace strings never match.
    /// </summary>
    /// <param name="first">The first client identifier string to compare.</param>
    /// <param name="second">The second client identifier string to compare.</param>
    /// <returns><see langword="true" /> if both refer to the same client.</returns>
    public static bool AreEqual(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && (
            string.Equals(first, second, StringComparison.Ordinal)
            || ClientIdentifierFormat.AreEqual(first, second)
        );
}
