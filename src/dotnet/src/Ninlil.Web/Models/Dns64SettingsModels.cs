// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the DNS64 settings (<c>Dns64Options</c>) that
///     <c>GET /api/dns64-settings</c> returns. Nothing in it is secret, so a <c>PATCH</c> to the
///     same endpoint accepts the same fields.
/// </summary>
/// <param name="Enable">Whether AAAA records are synthesized for IPv4-only names. Defaults to <see langword="false" />.</param>
/// <param name="SynthesisPrefix">The IPv6 prefix synthesized addresses are built in. Defaults to the well-known <c>64:ff9b::/96</c>, under which only globally reachable IPv4 addresses are synthesized. A prefix with a non-zero "u" octet (bits 64 to 71), inside <c>::ffff:0:0/96</c> or multicast is rejected.</param>
public record Dns64SettingsDetail(bool Enable, string SynthesisPrefix);
