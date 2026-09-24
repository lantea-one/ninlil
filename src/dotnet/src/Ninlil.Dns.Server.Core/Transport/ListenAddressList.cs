using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This static class reads and validates the <c>ListenAddresses</c> setting every DNS
///     transport's options class carries (Plain, DNS-over-TLS, DNS-over-QUIC, DNSCrypt): the list
///     of local IP addresses that transport binds to.
///     <para>
///         <b>Why one setting per transport, not one shared list.</b> Every transport already has
///         its own options class with its own <c>Port</c>, and operators commonly want different
///         exposure per transport -- plain DNS only on the LAN, DNS-over-TLS on the WAN too. A
///         per-transport list sits next to the port it pairs with, and needs no precedence rules
///         between a shared list and overrides.
///     </para>
///     <para>
///         <b>Why a comma-separated string, not a JSON array.</b> The setting is a single string
///         such as <c>"127.0.0.1, ::1"</c>. A list-typed option would get no <c>NINLIL_</c>
///         environment variable (the environment mapping skips lists, see
///         <c>OptionsEnvironmentVariableMap</c>), and choosing which interfaces a container
///         listens on is exactly the kind of thing set from the environment. The same
///         comma-joined-scalar shape <c>TlsCertificateOptions.AllowedProtocols</c> already uses.
///         The REST API still exposes it as a proper array.
///     </para>
///     <para>
///         <b>Only IP literals are accepted</b>, not interface names such as <c>eth0</c>. An
///         interface name could only be resolved once, when the listener binds; an interface that
///         gets its address later (DHCP, a VPN coming up, hot-plug) would then be silently skipped
///         until the next restart, which is worse than asking for the address up front.
///     </para>
///     <para>
///         An empty value means "every address", served by one dual-stack socket exactly as before
///         this setting existed, so existing installations are unaffected.
///     </para>
/// </summary>
public static partial class ListenAddressList
{
    /// <summary>
    ///     This constant contains the characters that separate entries. Whitespace is accepted as
    ///     well as commas so a hand-typed <c>"127.0.0.1 ::1"</c> behaves as expected.
    /// </summary>
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>
    ///     This method splits a configured <c>ListenAddresses</c> value into its raw entries,
    ///     trimmed, with empty entries dropped. Nothing is validated here.
    /// </summary>
    /// <param name="configured">The configured value; <see langword="null" /> is treated as empty.</param>
    /// <returns>Every non-empty entry, in the order it was written.</returns>
    public static IReadOnlyList<string> Split(string configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? []
            : configured.Split(
                Separators,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

    /// <summary>
    ///     This method joins already-validated addresses back into the single string the
    ///     configuration file stores.
    /// </summary>
    public static string Join(IEnumerable<IPAddress> addresses) =>
        string.Join(", ", addresses.Select(address => address.ToString()));

    /// <summary>
    ///     This method parses one entry as an IPv4 or IPv6 literal, more strictly than
    ///     <see cref="IPAddress.TryParse(string, out IPAddress)" /> alone: that method also accepts
    ///     shorthand such as <c>"127.1"</c> or <c>"1"</c> (read as <c>0.0.0.1</c>) and a
    ///     bracketed IPv6 address with a port (<c>"[::1]:53"</c>, port silently dropped), none of
    ///     which an operator typing a listen address means. An IPv4-mapped IPv6 address
    ///     (<c>::ffff:192.0.2.1</c>) is normalized to plain IPv4, since that is what it names and
    ///     what the listener has to bind.
    /// </summary>
    /// <param name="entry">One trimmed entry.</param>
    /// <param name="address">The parsed address, when this method returns <see langword="true" />.</param>
    /// <returns>Whether <paramref name="entry" /> is a well-formed IP literal.</returns>
    public static bool TryParseAddress(string entry, out IPAddress address)
    {
        address = null;

        if (string.IsNullOrWhiteSpace(entry))
            return false;

        bool looksLikeIPv6 = entry.Contains(':');

        if (!looksLikeIPv6 && !DottedQuad().IsMatch(entry))
            return false;

        if (looksLikeIPv6 && (entry.Contains('[') || entry.Contains(']')))
            return false;

        if (!IPAddress.TryParse(entry, out IPAddress parsed))
            return false;

        address = parsed.IsIPv4MappedToIPv6 ? parsed.MapToIPv4() : parsed;
        return true;
    }

    /// <summary>
    ///     This method validates a whole list strictly, the way the settings API needs to: every
    ///     entry must parse, and no entry may already be covered by a wildcard of the same family
    ///     in the same list (<c>0.0.0.0</c> plus <c>127.0.0.1</c> would make the second bind fail
    ///     with "address in use", since the wildcard socket already owns that port on every IPv4
    ///     address). <c>0.0.0.0</c> together with <c>::</c> is fine: each explicit IPv6 socket is
    ///     IPv6-only, so the two wildcards never overlap. Duplicates are dropped.
    /// </summary>
    /// <param name="entries">The raw entries.</param>
    /// <param name="addresses">The normalized, de-duplicated addresses, when valid.</param>
    /// <param name="error">A message naming the offending entry, when invalid.</param>
    /// <returns>Whether every entry is valid.</returns>
    public static bool TryValidate(
        IEnumerable<string> entries,
        out List<IPAddress> addresses,
        out string error
    )
    {
        addresses = [];
        error = null;

        foreach (string rawEntry in entries ?? [])
        {
            string entry = rawEntry?.Trim();

            if (!TryParseAddress(entry, out IPAddress address))
            {
                error =
                    $"'{rawEntry}' is not a valid IP address. Listen addresses must be IPv4 or IPv6 "
                    + "literals such as 192.168.1.1, ::1, 0.0.0.0 or :: -- no host names, "
                    + "interface names or ports.";
                addresses = [];
                return false;
            }

            if (!addresses.Contains(address))
                addresses.Add(address);
        }

        foreach (IPAddress address in addresses)
        {
            IPAddress wildcard = WildcardFor(address.AddressFamily);

            if (!address.Equals(wildcard) && addresses.Contains(wildcard))
            {
                error =
                    $"'{address}' is already covered by '{wildcard}', which listens on every "
                    + $"{FamilyName(address.AddressFamily)} address. Remove one of them.";
                addresses = [];
                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     This method resolves a configured value into the addresses a listener should actually
    ///     bind, leniently: a listener cannot refuse its configuration the way the settings API
    ///     can, so an unusable entry is skipped and reported instead, and every other entry is
    ///     still served. An address already covered by a same-family wildcard in the list is
    ///     dropped (its traffic is served by the wildcard socket anyway) rather than left to fail
    ///     with "address in use".
    /// </summary>
    /// <param name="configured">The configured <c>ListenAddresses</c> value.</param>
    /// <returns>
    ///     The resolution: <see cref="ListenAddressResolution.ListensEverywhere" /> when nothing is
    ///     configured, otherwise the addresses to bind plus every entry that could not be used.
    /// </returns>
    public static ListenAddressResolution Resolve(string configured)
    {
        IReadOnlyList<string> entries = Split(configured);

        if (entries.Count == 0)
            return new ListenAddressResolution(true, [], [], []);

        List<IPAddress> addresses = [];
        List<string> invalidEntries = [];

        foreach (string entry in entries)
        {
            if (!TryParseAddress(entry, out IPAddress address))
                invalidEntries.Add(entry);
            else if (!addresses.Contains(address))
                addresses.Add(address);
        }

        List<IPAddress> covered = [];

        foreach (IPAddress address in addresses)
        {
            IPAddress wildcard = WildcardFor(address.AddressFamily);

            if (!address.Equals(wildcard) && addresses.Contains(wildcard))
                covered.Add(address);
        }

        addresses.RemoveAll(covered.Contains);

        return new ListenAddressResolution(false, addresses, invalidEntries, covered);
    }

    /// <summary>This method returns the "any address" wildcard for an address family.</summary>
    private static IPAddress WildcardFor(AddressFamily family) =>
        family == AddressFamily.InterNetworkV6 ? IPAddress.IPv6Any : IPAddress.Any;

    /// <summary>This method names an address family for an operator-facing message.</summary>
    private static string FamilyName(AddressFamily family) =>
        family == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";

    /// <summary>
    ///     This regular expression matches exactly four dot-separated decimal groups without
    ///     leading zeros -- the only IPv4 spelling an operator means by a listen address. Leading
    ///     zeros are refused because the underlying parser reads them as octal (<c>010.0.0.1</c>
    ///     would quietly become <c>8.0.0.1</c>).
    /// </summary>
    [GeneratedRegex(@"^(0|[1-9]\d{0,2})(\.(0|[1-9]\d{0,2})){3}$")]
    private static partial Regex DottedQuad();
}

/// <summary>
///     This record is what <see cref="ListenAddressList.Resolve" /> returns for a listener about to
///     bind.
/// </summary>
/// <param name="ListensEverywhere">
///     Whether nothing was configured, meaning "bind every address with one dual-stack socket",
///     the behavior from before this setting existed. When <see langword="false" /> and
///     <paramref name="Addresses" /> is empty, every configured entry was unusable and the
///     listener must not bind anywhere -- falling back to every address would expose the
///     transport on exactly the interfaces the operator tried to keep it off.
/// </param>
/// <param name="Addresses">The addresses to bind, one socket each.</param>
/// <param name="InvalidEntries">Every configured entry that is not a valid IP literal.</param>
/// <param name="CoveredAddresses">Every address dropped because a same-family wildcard in the list already covers it.</param>
public sealed record ListenAddressResolution(
    bool ListensEverywhere,
    IReadOnlyList<IPAddress> Addresses,
    IReadOnlyList<string> InvalidEntries,
    IReadOnlyList<IPAddress> CoveredAddresses
);
