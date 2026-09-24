using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Controllers;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a global upstream forwarder
///     (<c>POST</c>/<c>PUT /api/forwarders</c>).
/// </summary>
/// <param name="Name">An operator-chosen, human-readable, unique name for this forwarder.</param>
/// <param name="DefinitionString">
///     The forwarder's own Technitium/AdGuard Home-style definition string -- a bare address, a
///     <c>udp://</c>/<c>tcp://</c>/<c>tls://</c>/<c>https://</c>/<c>quic://</c> URL, or an
///     <c>sdns://</c> DNSCrypt stamp -- validated against the real
///     <c>IUpstreamForwarderDefinitionParser</c> (and, for an <c>sdns://</c> stamp,
///     <c>DnsCryptStampParser</c>) before this forwarder is ever persisted, rather than accepting
///     an arbitrary string, the same "validate against the real codec/parser" discipline
///     <see cref="RecordsController" /> already applies to a record's own <c>data</c> field.
/// </param>
/// <param name="SortOrder">This forwarder's own position among every other configured global forwarder -- lower values are preferred first.</param>
/// <param name="IsDisabled">Whether the forwarder starts out (or is currently) disabled.</param>
/// <param name="Comments">Free-form operator notes about this forwarder.</param>
public record ForwarderWriteRequest(
    string Name,
    string DefinitionString,
    int SortOrder = 0,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a single global upstream forwarder's own fields, for list, single-item,
///     and creation responses. No secret material is involved anywhere in this entity -- a
///     forwarder definition string is not a credential -- so, unlike <c>TsigKeyDetail</c>/
///     <c>DnssecKeyDetail</c>/<c>CloudProviderCredentialDetail</c>, there is no reveal-once policy
///     to apply here at all.
/// </summary>
/// <param name="Id">This forwarder's own identifier.</param>
/// <param name="Name">This forwarder's own human-readable name.</param>
/// <param name="DefinitionString">This forwarder's own definition string, exactly as stored.</param>
/// <param name="Protocol">
///     The transport protocol <see cref="DefinitionString" /> parses to -- computed fresh at read
///     time via the same parser used to validate it at write time, purely as a read-only
///     convenience for a caller that would otherwise have to parse the string itself to know.
/// </param>
/// <param name="SortOrder">This forwarder's own position among every other configured global forwarder.</param>
/// <param name="IsDisabled">Whether this forwarder is currently disabled.</param>
/// <param name="Comments">Free-form operator notes about this forwarder.</param>
/// <param name="CreatedAtUtc">When this forwarder was created.</param>
/// <param name="UpdatedAtUtc">When this forwarder was last modified, or <see langword="null" /> if never.</param>
/// <param name="OriginatingNodeId">
///     The cluster node this forwarder was last created or modified on (the cluster's own
///     operational config sync), or <see langword="null" /> on a deployment that has never had clustering
///     enabled.
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
