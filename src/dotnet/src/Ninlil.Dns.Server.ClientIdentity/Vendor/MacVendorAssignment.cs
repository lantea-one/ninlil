// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This record is one IEEE MAC address block assignment: a prefix of the length its
///     <see cref="Registry" /> sets, and the organization it is assigned to.
///     <see cref="MacVendorRegistryCsvParser" /> and <see cref="MacVendorSnapshotCodec" /> produce
///     it, and <see cref="MacVendorTable" /> is built from it.
/// </summary>
/// <param name="Registry">The registry the assignment belongs to, which fixes its prefix length.</param>
/// <param name="Prefix">The prefix itself, as the unsigned number its hexadecimal digits spell.</param>
/// <param name="Organization">
///     The assigned organization's name as the IEEE publishes it, trimmed and with control
///     characters replaced by spaces.
/// </param>
public readonly record struct MacVendorAssignment(
    MacVendorRegistryEnum Registry,
    ulong Prefix,
    string Organization
)
{
    /// <summary>
    ///     This method formats <see cref="Prefix" /> as the fixed-width, uppercase hexadecimal
    ///     string the IEEE CSV files use for it.
    /// </summary>
    /// <returns>The prefix, for example <c>286FB9</c> for an MA-L assignment.</returns>
    public string FormatPrefix() => Prefix.ToString("X" + Registry.PrefixHexDigits);
}
