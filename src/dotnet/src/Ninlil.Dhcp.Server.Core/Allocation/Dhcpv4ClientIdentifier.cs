using Ninlil.Dhcp.Common.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Core.Allocation;

/// <summary>
///     This static class computes and formats the identifier this engine uses, throughout, to
///     recognize "the same client" across an exchange -- the key every
///     <see cref="Data.Abstractions.Entity.DhcpReservationEntity.ClientIdentifier" /> and
///     <see cref="Data.Abstractions.Entity.LeaseEntity.ClientIdentifier" /> value is compared
///     against.
///     <para>
///         RFC 2131 Section 4.2 says a server identifies a client by its Client-identifier option
///         (tag 61) when the client sends one, and by its hardware address (<c>chaddr</c>)
///         otherwise -- the overwhelmingly common case on a real network, since ordinary consumer
///         and IoT devices rarely send a Client-identifier option at all. This project therefore
///         computes the identifier as: the raw Client-identifier option bytes when present, or the
///         raw <c>chaddr</c> bytes alone otherwise -- deliberately *not* prefixed with a hardware
///         type octet the way RFC 2132 Section 9.14 encodes a Client-identifier option's own value
///         when a client chooses to send one. That RFC section describes the option's own wire
///         encoding, not a requirement that a server without one must synthesize an equivalent
///         type-prefixed value for its own internal bookkeeping -- and a plain <c>chaddr</c>-only
///         identifier is what lets a static reservation genuinely be configured "by MAC address"
///         (this project's own stated design goal): an operator enters the exact same plain MAC
///         address (<c>aa:bb:cc:dd:ee:ff</c>) into a <see cref="Data.Abstractions.Entity
///         .DhcpReservationEntity.ClientIdentifier" /> field that a packet capture or a router's
///         own client list would show, with no extra leading byte to account for.
///     </para>
///     <para>
///         The identifier is stored and compared as a colon-separated, lowercase hexadecimal
///         string (for example <c>aa:bb:cc:dd:ee:ff</c>) -- the overwhelmingly common
///         human-readable MAC address notation an operator would actually type into a
///         reservation's own <see cref="Data.Abstractions.Entity.DhcpReservationEntity
///         .ClientIdentifier" /> field, rather than a raw byte array or an ambiguous
///         separator-free hex run. <see cref="AreEqual" /> normalizes both sides (stripping any
///         separator and case difference) before comparing, so an operator entering
///         <c>AA-BB-CC-DD-EE-FF</c> or <c>aabbccddeeff</c> still matches correctly -- this
///         engine's own client-facing storage format is a convenience for the operator, not a
///         strict wire-format requirement.
///     </para>
/// </summary>
internal static class Dhcpv4ClientIdentifier
{
    /// <summary>
    ///     This method computes a request's own effective client identifier bytes, per this
    ///     class's own doc comment.
    /// </summary>
    /// <param name="request">The request to compute an identifier for.</param>
    /// <returns>The effective client identifier's raw bytes.</returns>
    public static byte[] Resolve(Dhcpv4Message request) =>
        request.TryGetClientIdentifier(out byte[] clientIdentifierOption)
            ? clientIdentifierOption
            : request.ClientHardwareAddress;

    /// <summary>
    ///     This method formats a request's own effective client identifier (see
    ///     <see cref="Resolve" />) as the colon-separated lowercase hexadecimal string this
    ///     engine stores and compares client identifiers as.
    /// </summary>
    /// <param name="request">The request to format an identifier for.</param>
    /// <returns>The formatted client identifier string.</returns>
    public static string Format(Dhcpv4Message request) => Format(Resolve(request));

    /// <summary>
    ///     This method formats raw identifier bytes as the colon-separated lowercase hexadecimal
    ///     string this engine stores and compares client identifiers as.
    /// </summary>
    /// <param name="identifier">The raw identifier bytes to format.</param>
    /// <returns>The formatted client identifier string.</returns>
    public static string Format(byte[] identifier) =>
        string.Join(':', identifier.Select(b => b.ToString("x2")));

    /// <summary>
    ///     This method reports whether two stored client identifier strings refer to the same
    ///     client, tolerating the separator/case differences an operator might type a reservation's
    ///     own identifier in -- see this class's own doc comment.
    /// </summary>
    /// <param name="first">The first client identifier string to compare.</param>
    /// <param name="second">The second client identifier string to compare.</param>
    /// <returns><see langword="true" /> if both refer to the same client.</returns>
    public static bool AreEqual(string first, string second) =>
        !string.IsNullOrWhiteSpace(first)
        && !string.IsNullOrWhiteSpace(second)
        && string.Equals(Normalize(first), Normalize(second), StringComparison.Ordinal);

    /// <summary>
    ///     This method strips every non-hexadecimal character from a client identifier string and
    ///     lowercases what remains, so two differently-formatted but equivalent identifiers
    ///     compare equal.
    /// </summary>
    private static string Normalize(string value) =>
        new(value.Where(Uri.IsHexDigit).Select(char.ToLowerInvariant).ToArray());
}
