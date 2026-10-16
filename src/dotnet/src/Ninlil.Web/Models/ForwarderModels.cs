using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a global upstream forwarder
///     (<c>POST /api/forwarders</c>, <c>PUT /api/forwarders/{id}</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for the forwarder.</param>
/// <param name="DefinitionString">
///     The forwarder definition, in the style Technitium and AdGuard Home use: a bare address; a
///     <c>udp://</c>, <c>tcp://</c>, <c>tls://</c>, <c>https://</c> or <c>quic://</c> URL; or an
///     <c>sdns://</c> DNSCrypt stamp. An IPv6 address with a port is written in brackets, for example
///     <c>[2001:db8::1]:53</c>. The syntax is checked with <c>IUpstreamForwarderDefinitionParser</c>
///     (and <c>DnsCryptStampParser</c> for a stamp) before it is saved, much as
///     <see cref="RecordsController" /> checks a record's <c>data</c>. A host name is not looked up
///     at that point, so one that does not resolve is still saved; the server resolves it in the
///     background and again about every five minutes.
/// </param>
/// <param name="SortOrder">The forwarder's position among the global forwarders; lower values are tried first. Defaults to 0.</param>
/// <param name="IsDisabled">Whether the forwarder is disabled.</param>
/// <param name="Comments">Free-form operator notes about the forwarder.</param>
public record ForwarderWriteRequest(
    string Name,
    string DefinitionString,
    int SortOrder = 0,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a global upstream forwarder, as returned by the list, get and write
///     endpoints of <c>/api/forwarders</c>. A definition string is not a credential, so there is
///     no reveal-once handling as with <c>TsigKeyDetail</c> or
///     <c>CloudProviderCredentialDetail</c>.
/// </summary>
/// <param name="Id">The forwarder's identifier.</param>
/// <param name="Name">The forwarder's human-readable name.</param>
/// <param name="DefinitionString">The forwarder's definition string, as stored.</param>
/// <param name="Protocol">
///     The transport protocol <see cref="DefinitionString" /> parses to, computed when the
///     response is built with the parser that validated it, so a caller need not parse the string;
///     <see langword="null" /> if it no longer parses.
/// </param>
/// <param name="SortOrder">The forwarder's position among the global forwarders; lower values are tried first.</param>
/// <param name="IsDisabled">Whether the forwarder is disabled.</param>
/// <param name="Comments">Free-form operator notes about the forwarder.</param>
/// <param name="CreatedAtUtc">When the forwarder was created.</param>
/// <param name="UpdatedAtUtc">When the forwarder was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node the forwarder was last created or modified on, used by cluster config
///     sync, or <see langword="null" /> if it was never written with clustering enabled.
/// </param>
public record ForwarderDetail(
    Guid Id,
    string Name,
    string DefinitionString,
    UpstreamForwarderProtocolEnum? Protocol,
    int SortOrder,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    Guid? OriginatingNodeId = null
);
