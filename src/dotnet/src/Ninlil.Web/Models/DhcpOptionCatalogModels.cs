using Ninlil.Dhcp.Common.OptionCatalog;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of one <see cref="DhcpOptionCatalogEntry" />, as returned by
///     <c>GET /api/dhcp-option-catalog</c>. It copies the catalog record field by field so the API
///     does not change when the record does (see
///     <see cref="Controllers.DhcpOptionCatalogController" />).
/// </summary>
/// <param name="Code">The option's numeric code (see <see cref="DhcpOptionCatalogEntry.Code" />).</param>
/// <param name="Name">A short, human-readable name for the option.</param>
/// <param name="Description">A longer description of what the option carries and which RFC section defines it.</param>
/// <param name="ValueType">The kind of value the defining RFC gives the option, which picks the UI's value editor.</param>
/// <param name="IsServerAddressEligible">
///     Whether the UI should offer "use this server's address" as an alternative to typing a
///     value.
/// </param>
public record DhcpOptionCatalogEntryDetail(
    int Code,
    string Name,
    string Description,
    DhcpOptionValueTypeEnum ValueType,
    bool IsServerAddressEligible
);
