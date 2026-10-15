using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a manual mapping from a
///     source address to a client identity (<c>POST /api/client-static-address-mappings</c>,
///     <c>PUT /api/client-static-address-mappings/{id}</c>).
/// </summary>
/// <param name="Cidr">
///     The source address or network mapped to <paramref name="ClientIdentifier" />: a bare IP
///     address (exact match) or a CIDR network with a <c>/</c> (matches every address in it). A
///     value with a <c>/</c> must parse with
///     <see cref="System.Net.IPNetwork.TryParse(string, out System.Net.IPNetwork)" /> and one
///     without with <see cref="System.Net.IPAddress.TryParse(string, out System.Net.IPAddress)" />,
///     the same two forms
///     <c>Ninlil.Dns.Server.ClientIdentity.StaticAddressMappingClientIdentityResolver</c> matches
///     at query time.
/// </param>
/// <param name="ClientIdentifier">
///     The persistent client identifier a query from <paramref name="Cidr" /> resolves to: a
///     MAC address for an <see cref="InternetProtocolVersionEnum.IPv4" /> mapping, or a DHCP
///     Unique Identifier (DUID) for an <see cref="InternetProtocolVersionEnum.IPv6" /> one, in
///     hex. Accepted spellings are <c>aa:bb:cc:dd:ee:ff</c>, <c>aa-bb-cc-dd-ee-ff</c>,
///     <c>aabb.ccdd.eeff</c> and <c>aabbccddeeff</c> (up to 85 bytes); it is stored and returned
///     as <c>aa:bb:cc:dd:ee:ff</c>.
/// </param>
/// <param name="ProtocolVersion">
///     Which identifier space <paramref name="ClientIdentifier" /> belongs to. It must match the
///     address family of <paramref name="Cidr" />.
/// </param>
/// <param name="IsDisabled">Whether the mapping is disabled.</param>
/// <param name="Comments">Free-text operator notes about the mapping.</param>
public record DnsClientStaticAddressMappingWriteRequest(
    string Cidr,
    string ClientIdentifier,
    InternetProtocolVersionEnum ProtocolVersion,
    bool IsDisabled = false,
    string Comments = null
);

/// <summary>
///     This class carries a static address mapping, as returned by the list, get and write
///     endpoints of <c>/api/client-static-address-mappings</c>.
/// </summary>
/// <param name="Id">The mapping's identifier.</param>
/// <param name="Cidr">The mapping's source address or network, as stored.</param>
/// <param name="ClientIdentifier">The persistent client identifier the mapping resolves to.</param>
/// <param name="ProtocolVersion">Which identifier space <see cref="ClientIdentifier" /> belongs to.</param>
/// <param name="IsDisabled">
///     Whether the mapping is disabled. <c>StaticAddressMappingClientIdentityResolver</c> ignores
///     a disabled mapping.
/// </param>
/// <param name="Comments">Free-text operator notes about the mapping.</param>
/// <param name="CreatedAtUtc">When the mapping was created.</param>
/// <param name="UpdatedAtUtc">When the mapping was last modified, or <see langword="null" /> if never.</param>
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
