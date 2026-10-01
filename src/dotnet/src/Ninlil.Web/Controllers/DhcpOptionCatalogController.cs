using Microsoft.AspNetCore.Mvc;
using Ninlil.Dhcp.Common.OptionCatalog;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Web.Examples.DhcpOptionCatalog;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller exposes the typed DHCP option catalogs, <see cref="Dhcpv4OptionCatalog" />
///     and <see cref="Dhcpv6OptionCatalog" /> (see each for its IANA and RFC sources). The admin
///     console's scope and tag-rule forms use it to offer an option list per protocol, pick a
///     value editor (an address field, a duration picker, a text box) from
///     <see cref="DhcpOptionCatalogEntryDetail.ValueType" />, and offer "use this server's
///     address" where <see cref="DhcpOptionCatalogEntryDetail.IsServerAddressEligible" /> allows,
///     without hard-coding that knowledge in the UI.
///     <para>
///         <b>It is read only.</b> The catalog is compiled into the server, not stored, so there
///         is nothing to create, update or delete. Options not in the catalog can still be set
///         by raw tag and hex value through <c>DhcpScopeCustomOptionEntity</c> and
///         <c>DhcpTagRuleOptionEntity</c>.
///     </para>
///     <para>
///         <see cref="DhcpOptionCatalogEntryDetail" /> copies <see cref="DhcpOptionCatalogEntry" />
///         field for field rather than serializing the catalog record directly, as with
///         <c>PresetSummary</c> in <see cref="PresetsController" />, so a change on either side
///         does not silently change the API.
///     </para>
///     <para>
///         It reveals nothing secret, configured or live, only metadata that is the same on every
///         installation, so it needs only <c>dhcpoptioncatalog:read</c>; there is no write or
///         manage permission.
///     </para>
/// </summary>
[ApiController]
[Route("api/dhcp-option-catalog")]
public class DhcpOptionCatalogController : ControllerBase
{
    /// <summary>
    ///     This action returns every catalog entry for one protocol, in the catalog's ascending
    ///     tag or code order. Returns 400 when <c>protocolVersion</c> is missing or not
    ///     <c>IPv4</c> or <c>IPv6</c>. Requires <c>dhcpoptioncatalog:read</c>.
    /// </summary>
    /// <param name="protocolVersion">
    ///     Which protocol's catalog to return: <c>IPv4</c> or <c>IPv6</c>. An unknown value fails
    ///     model binding with 400. A missing value binds to the enum's zero value, which is neither
    ///     member (they are 4 and 6), so the action rejects it with a 400 rather than defaulting to
    ///     one protocol.
    /// </param>
    [HttpGet]
    [RequirePermission("dhcpoptioncatalog:read")]
    [SwaggerResponseExample(
        StatusCodes.Status200OK,
        typeof(DhcpOptionCatalogEntryDetailListExample)
    )]
    public ActionResult<IReadOnlyList<DhcpOptionCatalogEntryDetail>> List(
        [FromQuery] InternetProtocolVersionEnum protocolVersion
    )
    {
        IReadOnlyList<DhcpOptionCatalogEntry> entries = protocolVersion switch
        {
            InternetProtocolVersionEnum.IPv4 => Dhcpv4OptionCatalog.Entries,
            InternetProtocolVersionEnum.IPv6 => Dhcpv6OptionCatalog.Entries,
            _ => throw new ValidationException(
                "The protocolVersion query parameter is required and must be either "
                    + "\"IPv4\" or \"IPv6\"."
            ),
        };

        return Ok(
            entries
                .Select(entry => new DhcpOptionCatalogEntryDetail(
                    entry.Code,
                    entry.Name,
                    entry.Description,
                    entry.ValueType,
                    entry.IsServerAddressEligible
                ))
                .ToList()
        );
    }
}
