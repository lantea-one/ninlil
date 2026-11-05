using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the fields used to create or fully replace a manual mapping from a
///     source address to a client identity (<c>POST /api/client-static-address-mappings</c>,
///     <c>PUT /api/client-static-address-mappings/{id}</c>).
/// </summary>
/// <param name="Cidr">
///     The source address or network mapped to <c>clientIdentifier</c>: a bare IP
///     address (exact match) or a CIDR network with a <c>/</c> (matches every address in it). A
///     value with a <c>/</c> must be a valid IPv4 or IPv6 CIDR prefix and one without must be a
///     valid IPv4 or IPv6 address, the same two forms the server matches at query time.
/// </param>
/// <param name="ClientIdentifier">
///     The persistent client identifier a query from <c>cidr</c> resolves to: a
///     MAC address for an <c>IPv4</c> mapping, or a DHCP
///     Unique Identifier (DUID) for an <c>IPv6</c> one, in
///     hex. Accepted spellings are <c>aa:bb:cc:dd:ee:ff</c>, <c>aa-bb-cc-dd-ee-ff</c>,
///     <c>aabb.ccdd.eeff</c> and <c>aabbccddeeff</c> (up to 85 bytes); it is stored and returned
///     as <c>aa:bb:cc:dd:ee:ff</c>.
/// </param>
/// <param name="ProtocolVersion">
///     Which identifier space <c>clientIdentifier</c> belongs to. It must match the
///     address family of <c>cidr</c>.
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
/// <param name="ProtocolVersion">Which identifier space <c>clientIdentifier</c> belongs to.</param>
/// <param name="IsDisabled">
///     Whether the mapping is disabled. A disabled mapping is ignored when a client is identified.
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
