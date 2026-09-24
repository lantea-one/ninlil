// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>DnsCookieOptions</c> (RFC 7873). No secret material is involved, so this record's own
///     fields are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
public record DnsCookieSettingsDetail(
    bool Enable,
    bool Require,
    TimeSpan SecretRotationInterval,
    DateTimeOffset? LastModifiedUtc = null,
    Guid? OriginatingNodeId = null
);
