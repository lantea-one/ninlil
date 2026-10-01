using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class computes and formats the key the DHCPv6 engine uses to recognize a
///     client, the counterpart of <see cref="Dhcpv4ClientIdentifier" />.
///     <para>
///         The key is the client's DUID from the Client Identifier option, which RFC 8415 Section
///         21.2 requires in the client messages this engine handles. There is no fallback:
///         <see cref="Resolve" /> returns <see langword="null" /> when the option is missing, and
///         <see cref="Dhcpv6RequestHandler" /> then ignores the message.
///     </para>
///     <para>
///         Formatting uses <see cref="Dhcpv4ClientIdentifier.Format(byte[])" />, which works on
///         any bytes despite the class name, so a DUID is stored in the same colon-separated
///         lowercase hex as a MAC address.
///     </para>
/// </summary>
internal static class Dhcpv6ClientIdentifier
{
    /// <summary>
    ///     This method returns the request's Client Identifier option (DUID) bytes, or
    ///     <see langword="null" /> if it has none.
    /// </summary>
    /// <param name="request">The request to resolve an identifier for.</param>
    /// <returns>The client's DUID bytes, or <see langword="null" />.</returns>
    public static byte[] Resolve(Dhcpv6Message request) =>
        request.TryGetClientIdentifier(out byte[] duid) ? duid : null;

    /// <summary>
    ///     This method formats the request's DUID (see <see cref="Resolve" />) as colon-separated
    ///     lowercase hex, or returns <see langword="null" /> if it has none.
    /// </summary>
    /// <param name="request">The request to format an identifier for.</param>
    /// <returns>The formatted client identifier string, or <see langword="null" />.</returns>
    public static string Format(Dhcpv6Message request)
    {
        byte[] duid = Resolve(request);
        return duid is null ? null : Dhcpv4ClientIdentifier.Format(duid);
    }
}
