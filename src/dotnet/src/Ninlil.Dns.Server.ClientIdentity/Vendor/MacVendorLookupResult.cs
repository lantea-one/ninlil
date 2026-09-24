// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This record is the outcome of looking a vendor up from a client identifier.
/// </summary>
/// <param name="MacAddress">
///     The MAC address the identifier embeds, as lowercase colon-separated hexadecimal, or
///     <see langword="null" /> when the identifier embeds none (an opaque DUID, or a free-form
///     identifier an operator typed).
/// </param>
/// <param name="Vendor">
///     The organization the MAC address's block is assigned to, or <see langword="null" /> when
///     there is no MAC, the MAC is randomized, or no registry lists its block.
/// </param>
/// <param name="IsRandomized">
///     Whether the MAC address has its locally-administered bit set -- the kind of private,
///     randomized address modern phones and laptops present per network. Such an address is
///     assigned by the device itself rather than a manufacturer, so it is never looked up: its
///     prefix identifies nobody, and any registry hit would be a coincidence.
/// </param>
public sealed record MacVendorLookupResult(string MacAddress, string Vendor, bool IsRandomized)
{
    /// <summary>
    ///     This property contains the result for an identifier that embeds no MAC address.
    /// </summary>
    public static MacVendorLookupResult NoMacAddress { get; } = new(null, null, false);
}
