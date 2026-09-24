// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>TsigServiceOptions</c> -- the clock-skew tolerance written into every message this
///     server signs. Exposed as a plain <see cref="int" /> even though the underlying options
///     property is a <see cref="ushort" />, since JSON has no unsigned 16-bit numeric type; a
///     <c>PATCH</c> to the same endpoint rejects anything outside 0-65535. This record's own field
///     is the exact same one a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record TsigSettingsDetail(int DefaultFudgeSeconds);
