using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the credentials a caller signs in with.
/// </summary>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Password">The account's password.</param>
/// <param name="RememberMe">
///     With <see cref="RefreshTokenDeliveryEnum.Cookie" /> delivery, whether the session should
///     outlive the browser: a persistent cookie and the long refresh lifetime when
///     <see langword="true" />, otherwise a browser-session cookie and the short browser-session
///     lifetime. Ignored for <see cref="RefreshTokenDeliveryEnum.Body" /> delivery, whose sessions
///     are always long-lived.
/// </param>
/// <param name="RefreshTokenDelivery">
///     How the completed sign-in's refresh token is handed back; <c>Body</c> when omitted. With
///     <c>Cookie</c>, <see cref="LoginResponse.RefreshToken" /> is always <see langword="null" />.
/// </param>
public record LoginRequest(
    string UserName,
    string Password,
    bool RememberMe = false,
    RefreshTokenDeliveryEnum RefreshTokenDelivery = RefreshTokenDeliveryEnum.Body
);

/// <summary>
///     This class carries the outcome of a sign-in attempt. One of two shapes applies: either <see cref="RequiresTwoFactor" /> is <see langword="true" /> and
///     <see cref="MfaToken" /> is set (the caller must next call
///     <c>POST /api/auth/login/two-factor</c>), or a full <see cref="AccessToken" />/
///     <see cref="RefreshToken" /> pair is set because sign-in is already complete.
/// </summary>
/// <param name="RequiresTwoFactor">Whether a second authentication factor is still required.</param>
/// <param name="MfaToken">
///     An opaque, short-lived token identifying this pending sign-in, to be echoed back to
///     <c>POST /api/auth/login/two-factor</c>. Only set when <see cref="RequiresTwoFactor" /> is
///     <see langword="true" />.
/// </param>
/// <param name="AccessToken">The signed JSON Web Token access token, once sign-in is complete.</param>
/// <param name="ExpiresAtUtc">The UTC date and time <see cref="AccessToken" /> expires.</param>
/// <param name="RefreshToken">
///     The opaque refresh token, once sign-in is complete, or <see langword="null" /> when it was
///     delivered as a cookie.
/// </param>
public record LoginResponse(
    bool RequiresTwoFactor,
    string MfaToken,
    string AccessToken,
    DateTimeOffset? ExpiresAtUtc,
    string RefreshToken
);

/// <summary>
///     This class carries the second-factor code a caller submits to finish a sign-in that
///     <see cref="LoginResponse.RequiresTwoFactor" /> flagged as still pending.
/// </summary>
/// <param name="MfaToken">The opaque token <see cref="LoginResponse.MfaToken" /> returned.</param>
/// <param name="Code">The time-based (authenticator app) or e-mailed verification code.</param>
/// <param name="Provider">
///     Which second factor <paramref name="Code" /> comes from: <c>"Authenticator"</c>,
///     <c>"Email"</c> or <c>"RecoveryCode"</c>. A recovery code is one of the caller's single-use
///     fallback codes from <c>POST /api/auth/two-factor/recovery-codes/generate</c>; this call
///     redeems it, so it cannot be used again. Any other value is treated as
///     <c>"Authenticator"</c>.
/// </param>
/// <param name="RememberMe">The same choice <see cref="LoginRequest.RememberMe" /> describes, repeated for this final step.</param>
/// <param name="RefreshTokenDelivery">The same choice <see cref="LoginRequest.RefreshTokenDelivery" /> describes, repeated for this final step.</param>
public record TwoFactorLoginRequest(
    string MfaToken,
    string Code,
    string Provider,
    bool RememberMe = false,
    RefreshTokenDeliveryEnum RefreshTokenDelivery = RefreshTokenDeliveryEnum.Body
);

/// <summary>
///     This class carries a completed sign-in's issued credentials.
/// </summary>
/// <param name="AccessToken">The signed JSON Web Token access token.</param>
/// <param name="ExpiresAtUtc">The UTC date and time <see cref="AccessToken" /> expires.</param>
/// <param name="RefreshToken">
///     The opaque refresh token, or <see langword="null" /> when it was delivered as a cookie.
/// </param>
public record AuthenticationResult(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string RefreshToken
);

/// <summary>
///     This class carries the refresh token a caller exchanges for a new access token, or revokes
///     at sign-out.
/// </summary>
/// <param name="RefreshToken">
///     The refresh token to act on. Omit it (send <c>{}</c>) to act on the browser's refresh
///     cookie instead; the response then rotates that cookie rather than returning a token.
/// </param>
public record RefreshRequest(string RefreshToken = null);

/// <summary>
///     This class carries a token-based password reset, submitted from the "set new password"
///     landing page to <see cref="Controllers.AuthController.ResetPassword" />. The page serves
///     both the reset e-mail an administrator sends (<c>UsersController.ResetPassword</c> in
///     <c>EmailLink</c> mode) and the self-service
///     <see cref="Controllers.AuthController.ForgotPassword" /> request. Nobody is signed in at
///     this point, so the action is <c>[AllowAnonymous]</c> and <see cref="Token" /> with
///     <see cref="UserId" /> is its only proof of authorization.
/// </summary>
/// <param name="UserId">The account the reset applies to, carried in the landing page's link.</param>
/// <param name="Token">
///     The password-reset token from <c>UserManager&lt;TUser&gt;.GeneratePasswordResetTokenAsync</c>,
///     Base64Url-encoded because a raw Identity token can contain <c>+</c>, <c>/</c> and
///     <c>=</c>, which are unsafe in a URL query string. The action decodes it before checking it.
/// </param>
/// <param name="NewPassword">The new password to set, once <see cref="Token" /> checks out.</param>
public record ConfirmPasswordResetRequest(Guid UserId, string Token, string NewPassword);

/// <summary>
///     This class carries the e-mail address an anonymous, self-service Forgot Password request
///     submits to <see cref="Controllers.AuthController.ForgotPassword" />.
/// </summary>
/// <param name="Email">
///     The e-mail address to look up. The response never says whether it matches an account (see
///     <see cref="Controllers.AuthController.ForgotPassword" />).
/// </param>
public record ForgotPasswordRequest(string Email);

/// <summary>
///     This class carries the response of <see cref="Controllers.AuthController.ForgotPassword" />.
///     <see cref="Message" /> is the same whether or not a reset link was sent, so a caller cannot
///     tell the two apart.
/// </summary>
/// <param name="Message">A generic, user-facing confirmation message.</param>
public record ForgotPasswordResult(string Message);
