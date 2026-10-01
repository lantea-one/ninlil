// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the TSIG settings (<c>TsigServiceOptions</c>) that
///     <c>GET /api/tsig-settings</c> returns: the clock skew tolerance (fudge) written into every
///     message the server signs. A <c>PATCH</c> to the same endpoint accepts the same field.
/// </summary>
/// <param name="DefaultFudgeSeconds">
///     The fudge, in seconds. The options property is a <see cref="ushort" />, but it is shown as
///     an <see cref="int" /> because JSON has no unsigned 16-bit type; a <c>PATCH</c> rejects
///     values outside 0 to 65535.
/// </param>
public record TsigSettingsDetail(int DefaultFudgeSeconds);
