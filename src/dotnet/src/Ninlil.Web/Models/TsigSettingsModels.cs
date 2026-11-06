// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the TSIG settings (configuration section <c>DnsServer:Tsig</c>) that
///     <c>GET /api/tsig-settings</c> returns: the clock skew tolerance (fudge) written into every
///     message the server signs. A <c>PATCH</c> to the same endpoint accepts the same field.
/// </summary>
/// <param name="DefaultFudgeSeconds">
///     The fudge, in seconds, from 0 to 65535. A <c>PATCH</c> rejects values outside that range.
/// </param>
public record TsigSettingsDetail(int DefaultFudgeSeconds);
