using Ninlil.Dns.Common.Enum;

// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries the credentials a caller signs in with.
/// </summary>
/// <param name="UserName">The account's sign-in name.</param>
/// <param name="Password">The account's password.</param>
/// <param name="RememberMe">
///     With <c>Cookie</c> delivery, whether the session should
///     outlive the browser: a persistent cookie and the long refresh lifetime when
///     <see langword="true" />, otherwise a browser-session cookie and the short browser-session
///     lifetime. Ignored for <c>Body</c> delivery, whose sessions
///     are always long-lived.
/// </param>
/// <param name="RefreshTokenDelivery">
///     How the completed sign-in's refresh token is handed back; <c>Body</c> when omitted. With
///     <c>Cookie</c>, <c>LoginResponse.refreshToken</c> is always <see langword="null" />.
/// </param>
public record LoginRequest(
    string UserName,
    string Password,
    bool RememberMe = false,
    RefreshTokenDeliveryEnum RefreshTokenDelivery = RefreshTokenDeliveryEnum.Body
);

/// <summary>
///     This class carries the outcome of a sign-in attempt. One of two shapes applies: either
///     <c>requiresTwoFactor</c> is <see langword="true" /> and <c>mfaToken</c> is set (the caller
///     must next call <c>POST /api/auth/login/two-factor</c>), or a full <c>accessToken</c>/
///     <c>refreshToken</c> pair is set because sign-in is already complete.
/// </summary>
/// <param name="RequiresTwoFactor">Whether a second authentication factor is still required.</param>
/// <param name="MfaToken">
///     An opaque, short-lived token identifying this pending sign-in, to be echoed back to
///     <c>POST /api/auth/login/two-factor</c>. Only set when <c>requiresTwoFactor</c> is
///     <see langword="true" />.
/// </param>
/// <param name="AccessToken">The signed JSON Web Token access token, once sign-in is complete.</param>
/// <param name="ExpiresAtUtc">The UTC date and time <c>accessToken</c> expires.</param>
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
///     <c>LoginResponse.requiresTwoFactor</c> flagged as still pending.
/// </summary>
/// <param name="MfaToken">The opaque token <c>LoginResponse.mfaToken</c> returned.</param>
/// <param name="Code">The time-based (authenticator app) or e-mailed verification code.</param>
/// <param name="Provider">
///     Which second factor <c>code</c> comes from: <c>"Authenticator"</c>,
///     <c>"Email"</c> or <c>"RecoveryCode"</c>. A recovery code is one of the caller's single-use
///     fallback codes from <c>POST /api/auth/two-factor/recovery-codes/generate</c>; this call
///     redeems it, so it cannot be used again. Any other value is treated as
///     <c>"Authenticator"</c>.
/// </param>
/// <param name="RememberMe">The same choice <c>LoginRequest.rememberMe</c> describes, repeated for this final step.</param>
/// <param name="RefreshTokenDelivery">The same choice <c>LoginRequest.refreshTokenDelivery</c> describes, repeated for this final step.</param>
public record TwoFactorLoginRequest(
    string MfaToken,
    string Code,
    string Provider,
    bool RememberMe = false,
    RefreshTokenDeliveryEnum RefreshTokenDelivery = RefreshTokenDeliveryEnum.Body
);

/// <summary>
///     This class carries the request for an e-mailed second-factor code during the pending
///     second-factor step of a sign-in.
/// </summary>
/// <param name="MfaToken">The opaque token <c>LoginResponse.mfaToken</c> returned.</param>
public record TwoFactorEmailCodeRequest(string MfaToken);

/// <summary>
///     This class carries a completed sign-in's issued credentials.
/// </summary>
/// <param name="AccessToken">The signed JSON Web Token access token.</param>
/// <param name="ExpiresAtUtc">The UTC date and time <c>accessToken</c> expires.</param>
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
///     both the reset e-mail an administrator sends (<c>POST /api/users/{id}/reset-password</c> in
///     <c>EmailLink</c> mode) and the self-service
///     <see cref="Controllers.AuthController.ForgotPassword" /> request. Nobody is signed in at
///     this point, so the action needs no sign-in and <c>token</c> with
///     <c>userId</c> is its only proof of authorization.
/// </summary>
/// <param name="UserId">The account the reset applies to, carried in the landing page's link.</param>
/// <param name="Token">
///     The password-reset token ASP.NET Core Identity issued,
///     Base64Url-encoded because a raw Identity token can contain <c>+</c>, <c>/</c> and
///     <c>=</c>, which are unsafe in a URL query string. The action decodes it before checking it.
/// </param>
/// <param name="NewPassword">The new password to set, once <c>token</c> checks out.</param>
public record ConfirmPasswordResetRequest(Guid UserId, string Token, string NewPassword);

/// <summary>
///     This class carries what the admin console's "confirm e-mail change" page submits to
///     <see cref="Controllers.AuthController.ConfirmEmailChange" />, taken from the link e-mailed
///     to the new address. The action is anonymous because the link may be opened in a browser that
///     is not signed in; <c>token</c> is the proof.
/// </summary>
/// <param name="UserId">The account whose address changes, carried in the link.</param>
/// <param name="Email">The new address, carried in the link. The token is bound to it.</param>
/// <param name="Token">
///     The change-email token ASP.NET Core Identity issued,
///     Base64Url-encoded for the link, as <c>ConfirmPasswordResetRequest.token</c> is.
/// </param>
public record ConfirmEmailChangeRequest(Guid UserId, string Email, string Token);

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
///     <c>message</c> is the same whether or not a reset link was sent, so a caller cannot
///     tell the two apart.
/// </summary>
/// <param name="Message">A generic, user-facing confirmation message.</param>
public record ForgotPasswordResult(string Message);
