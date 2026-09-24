using Ninlil.Dhcp.Common.OptionCatalog;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of one <see cref="DhcpOptionCatalogEntry" /> --
///     see <see cref="Controllers.DhcpOptionCatalogController" />'s own doc comment for why this
///     project's read-only option-metadata catalog is exposed through a dedicated model rather
///     than serializing <see cref="DhcpOptionCatalogEntry" /> itself.
/// </summary>
/// <param name="Code">The option's own raw numeric tag/code -- see <see cref="DhcpOptionCatalogEntry.Code" />'s own doc comment.</param>
/// <param name="Name">A short, human-readable name for this option.</param>
/// <param name="Description">A longer, human-readable description of what this option carries and which RFC/section defines it.</param>
/// <param name="ValueType">The shape of value this option's own defining RFC gives it.</param>
/// <param name="IsServerAddressEligible">
///     Whether an operator picking this option's value should be offered "use this server's own
///     address" as an alternative to typing one in.
/// </param>
public record DhcpOptionCatalogEntryDetail(
    int Code,
    string Name,
    string Description,
    DhcpOptionValueTypeEnum ValueType,
    bool IsServerAddressEligible
);
