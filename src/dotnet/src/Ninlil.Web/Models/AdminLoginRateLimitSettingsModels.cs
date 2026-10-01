// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This record is the REST shape of the admin sign-in rate limiter settings
///     (<c>AdminLoginRateLimitOptions</c>) that <c>GET /api/admin-login-rate-limit-settings</c>
///     returns. Nothing in it is secret, so a <c>PATCH</c> to the same endpoint accepts the same
///     fields.
/// </summary>
/// <param name="Enable">Whether the limiter rejects anything. Defaults to <see langword="true" />.</param>
/// <param name="PermitLimit">How many sign-in attempts one client address may make within one <see cref="Window" />. Defaults to 5.</param>
/// <param name="Window">The fixed window <see cref="PermitLimit" /> is counted over, which is also the lockout length. Defaults to five minutes.</param>
public record AdminLoginRateLimitSettingsDetail(bool Enable, int PermitLimit, TimeSpan Window);
