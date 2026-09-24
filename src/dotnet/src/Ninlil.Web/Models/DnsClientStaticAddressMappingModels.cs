using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a manual static
///     address-to-client-identity mapping (<c>POST</c>/<c>PUT /api/client-static-address-mappings</c>).
/// </summary>
/// <param name="Cidr">
///     The source address or network this mapping pins to <paramref name="ClientIdentifier" /> --
///     a bare IP address (exact match only) or a <c>/</c>-qualified CIDR network (matches every
///     address it contains). Validated against
///     <see cref="System.Net.IPNetwork.TryParse(string, out System.Net.IPNetwork)" />/
///     <see cref="System.Net.IPAddress.TryParse(string, out System.Net.IPAddress)" /> (whichever
///     the value's own shape calls for), the same dual-format acceptance
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> itself
///     matches a query's own source address against at resolution time.
/// </param>
/// <param name="ClientIdentifier">
///     The persistent client identifier a query whose source address matches
///     <paramref name="Cidr" /> resolves to -- a colon-separated hardware (MAC) address for an
///     <see cref="InternetProtocolVersionEnum.IPv4" /> mapping, or a DHCP Unique Identifier (DUID)
///     for an <see cref="InternetProtocolVersionEnum.IPv6" /> one.
/// </param>
/// <param name="ProtocolVersion">
///     Which of the two disjoint identifier spaces <paramref name="ClientIdentifier" /> belongs
///     to -- validated to actually agree with <paramref name="Cidr" />'s own real address family.
/// </param>
/// <param name="IsDisabled">Whether the mapping starts out (or is currently) disabled.</param>
/// <param name="Comments">Free-text operator notes about this mapping.</param>
public record DnsClientStaticAddressMappingWriteRequest(
    string Cidr,
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a single static address mapping's own fields, for list, single-item,
///     and creation responses.
/// </summary>
/// <param name="Id">This mapping's own identifier.</param>
/// <param name="Cidr">This mapping's own source address or network, exactly as stored.</param>
/// <param name="ClientIdentifier">The persistent client identifier this mapping resolves to.</param>
/// <param name="ProtocolVersion">Which of the two disjoint identifier spaces <see cref="ClientIdentifier" /> belongs to.</param>
/// <param name="IsDisabled">
///     Whether this mapping is currently disabled -- a disabled mapping is never consulted by
///     <c>StaticAddressMappingClientIdentityResolver</c>.
/// </param>
/// <param name="Comments">Free-text operator notes about this mapping.</param>
/// <param name="CreatedAtUtc">When this mapping was created.</param>
/// <param name="UpdatedAtUtc">When this mapping was last modified, or <see langword="null" /> if never.</param>
public record DnsClientStaticAddressMappingDetail(
    Guid Id,
    string Cidr,
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion,
    bool IsDisabled,
    string Comments,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc
);
