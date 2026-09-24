using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This interface resolves the hardware vendor behind a client identifier or MAC address,
///     against the IEEE MAC address block registries. Lookups are pure in-memory reads against an
///     immutable table, cheap enough to run for every client on every read of the client list, so
///     a vendor is always computed at read time and never stored -- it cannot go stale, and picks
///     up a refreshed table the moment one is loaded.
/// </summary>
public interface IMacVendorLookupService
{
    /// <summary>
    ///     This property contains the table currently answering lookups.
    /// </summary>
    MacVendorTable Current { get; }

    /// <summary>
    ///     This method extracts the MAC address a stored client identifier embeds, if any, and
    ///     looks its vendor up.
    /// </summary>
    /// <param name="clientIdentifier">A stored identifier: a MAC or DHCPv4 client-id for IPv4, a DUID for IPv6.</param>
    /// <param name="protocolVersion">Which identifier space <paramref name="clientIdentifier" /> belongs to.</param>
    /// <returns>The lookup outcome; never <see langword="null" />.</returns>
    MacVendorLookupResult LookupClientIdentifier(
        string clientIdentifier,
        InternetProtocolVersionEnum protocolVersion
    );

    /// <summary>
    ///     This method looks a raw MAC address's vendor up.
    /// </summary>
    /// <param name="macAddress">A 6-octet MAC address.</param>
    /// <returns>The lookup outcome; never <see langword="null" />.</returns>
    MacVendorLookupResult LookupMacAddress(ReadOnlySpan<byte> macAddress);
}
