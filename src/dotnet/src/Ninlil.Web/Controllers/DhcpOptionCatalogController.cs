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
///     This controller exposes <see cref="Dhcpv4OptionCatalog" />/<see cref="Dhcpv6OptionCatalog" />
///     -- this project's own read-only, typed DHCP option-metadata catalog (see either class's own
///     doc comment for the real IANA/RFC sourcing and scope reasoning behind it) -- as a REST
///     resource for a future value-entry UI to consume: an option dropdown, grouped by protocol,
///     that can pick the right value editor for a given option (an address field, a duration
///     picker, a plain text box, and so on) from <see cref="DhcpOptionCatalogEntryDetail.ValueType" />
///     alone, and offer "use this server's own address" as a value source wherever
///     <see cref="DhcpOptionCatalogEntryDetail.IsServerAddressEligible" /> says so -- without
///     needing to hardcode any of that knowledge into the UI itself.
///     <para>
///         <b>This is purely a read-only lookup table, with no write action at all</b> -- this
///         catalog is compiled into this project's own binary, not stored data, so there is
///         nothing here for an operator to create/update/delete; a genuinely custom/unlisted option
///         tag remains fully supported the same way it always has, through
///         <c>DhcpScopeCustomOptionEntity</c>/<c>DhcpTagRuleOptionEntity</c>'s own raw tag+hex
///         shape, entirely unaffected by this controller's existence.
///     </para>
///     <para>
///         <b>Response shape</b>: <see cref="DhcpOptionCatalogEntryDetail" /> mirrors
///         <see cref="DhcpOptionCatalogEntry" /> field-for-field, following this project's own
///         established convention (see <see cref="PresetsController" />'s own <c>PresetSummary</c>)
///         of never serializing a domain/common-layer catalog record directly as a REST response
///         body, even when the two shapes are identical today -- keeping a REST-specific seam
///         between them means a future change to either side never silently becomes a breaking
///         wire-format change for the other.
///     </para>
///     <para>
///         <b>Permission</b>: this reveals no secret, no per-operator configuration, and no live
///         server state at all -- only this project's own compiled-in, identical-for-every-
///         installation option metadata -- so it is gated by a single, lighter <c>dhcpoptioncatalog:read</c>
///         permission, with no corresponding <c>:write</c>/<c>:manage</c> permission at all (there
///         is nothing to write or manage).
///     </para>
/// </summary>
[ApiController]
[Route("api/dhcp-option-catalog")]
public class DhcpOptionCatalogController : ControllerBase
{
    /// <summary>
    ///     This action returns every catalog entry for one protocol version -- the full contents of
    ///     <see cref="Dhcpv4OptionCatalog.Entries" /> or <see cref="Dhcpv6OptionCatalog.Entries" />,
    ///     mapped onto this controller's own REST response shape, in the same ascending tag/code
    ///     order the underlying catalog itself is declared in.
    /// </summary>
    /// <param name="protocolVersion">
    ///     Which protocol's own option catalog to return -- <c>IPv4</c> or <c>IPv6</c>. Required;
    ///     an unrecognized value is rejected by ASP.NET Core's own default model binding as an
    ///     ordinary <c>400</c>, before this action's own code ever runs, the same "no hand-written
    ///     validation needed for a closed, two-member choice" shape
    ///     <see cref="DhcpServiceControlController" />'s own <c>{kind}</c> route segment already
    ///     establishes. A genuinely <em>missing</em> query string value cannot be caught by model
    ///     binding alone -- a non-nullable enum parameter simply binds to its default,
    ///     zero-valued member, which is not a real <see cref="InternetProtocolVersionEnum" />
    ///     member at all (neither <c>IPv4</c> nor <c>IPv6</c> is declared with the value
    ///     <c>0</c>) -- so this action's own body checks for that explicitly and throws
    ///     <see cref="ValidationException" /> (also a <c>400</c>) rather than silently defaulting
    ///     to one protocol's own catalog.
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
