// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST representation of this host's own currently-effective
///     <c>AdminLoginRateLimitOptions</c>. No secret material is involved, so this record's own
///     fields are the exact same ones a <c>PATCH</c> to the same endpoint accepts.
/// </summary>
/// <param name="Enable">Whether this limiter is wired into the request pipeline at all.</param>
/// <param name="PermitLimit">How many sign-in attempts are allowed within a single <see cref="Window" />.</param>
/// <param name="Window">The fixed window <see cref="PermitLimit" /> is counted over, and this limiter's own effective lockout duration.</param>
public record AdminLoginRateLimitSettingsDetail(bool Enable, int PermitLimit, TimeSpan Window);
