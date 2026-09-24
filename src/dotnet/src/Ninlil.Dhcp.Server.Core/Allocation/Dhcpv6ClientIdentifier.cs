using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class computes and formats the identifier this engine uses, throughout, to
///     recognize "the same client" across a DHCPv6 exchange -- the DHCPv6 analogue of
///     <see cref="Dhcpv4ClientIdentifier" />.
///     <para>
///         Unlike DHCPv4, where a Client-identifier option is optional and a server falls back to
///         the raw hardware address, RFC 8415 Section 21.2 requires every client message this
///         engine handles to carry a Client Identifier option (a DUID) -- there is no fallback
///         identifier to compute if one is absent, so <see cref="Resolve" /> simply reports that
///         case as <see langword="null" /> for the caller (<see cref="Dhcpv6RequestHandler" />) to
///         treat the whole message as unanswerable, the same way it already treats any other
///         malformed request.
///     </para>
///     <para>
///         Formatting and comparison reuse <see cref="Dhcpv4ClientIdentifier" />'s own
///         colon-separated lowercase hexadecimal primitives directly (<see cref="Dhcpv4ClientIdentifier.Format(byte[])" />/
///         <see cref="Dhcpv4ClientIdentifier.AreEqual" />) rather than duplicating them -- neither
///         is actually DHCPv4-specific despite that class's own name; both operate purely on raw
///         bytes/strings, with no dependency on anything DHCPv4-only, and a DUID's own byte
///         representation formats into exactly the same "meaningful for an operator to type into a
///         reservation field" hex notation a MAC address does.
///     </para>
/// </summary>
internal static class Dhcpv6ClientIdentifier
{
    /// <summary>
    ///     This method resolves a request's own Client Identifier option (DUID) bytes, or
    ///     <see langword="null" /> if the request carries none -- see this class's own doc comment
    ///     for why there is no fallback identifier to compute in that case.
    /// </summary>
    /// <param name="request">The request to resolve an identifier for.</param>
    /// <returns>The client's own DUID bytes, or <see langword="null" />.</returns>
    public static byte[] Resolve(Dhcpv6Message request) =>
        request.TryGetClientIdentifier(out byte[] duid) ? duid : null;

    /// <summary>
    ///     This method formats a request's own Client Identifier option (see <see cref="Resolve" />)
    ///     as the colon-separated lowercase hexadecimal string this engine stores and compares
    ///     client identifiers as, or <see langword="null" /> if the request carries none.
    /// </summary>
    /// <param name="request">The request to format an identifier for.</param>
    /// <returns>The formatted client identifier string, or <see langword="null" />.</returns>
    public static string Format(Dhcpv6Message request)
    {
        byte[] duid = Resolve(request);
        return duid is null ? null : Dhcpv4ClientIdentifier.Format(duid);
    }
}
