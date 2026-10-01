// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the EDNS Client Subnet settings
///     (<c>EdnsClientSubnetOptions</c>) that <c>GET /api/client-subnet-settings</c> returns.
///     Nothing in it is secret, so a <c>PATCH</c> to the same endpoint accepts the same fields.
/// </summary>
/// <param name="Enable">Whether outbound queries carry an ECS option. Defaults to <see langword="false" />.</param>
/// <param name="SourcePrefixLengthIPv4">How many leading bits of an IPv4 address are sent upstream. Defaults to 24.</param>
/// <param name="SourcePrefixLengthIPv6">How many leading bits of an IPv6 address are sent upstream. Defaults to 56.</param>
/// <param name="OverrideAddress">
///     An address sent in every ECS option instead of the client's, or <see langword="null" />
///     (the default) to use the client's address.
/// </param>
public record ClientSubnetSettingsDetail(
    bool Enable,
    byte SourcePrefixLengthIPv4,
    byte SourcePrefixLengthIPv6,
    string OverrideAddress
);
