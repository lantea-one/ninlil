// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a new TOTP (RFC 6238) authenticator enrollment from
///     <c>POST /api/auth/two-factor/authenticator/setup</c>, before the caller confirms it by
///     sending a generated code to <c>POST /api/auth/two-factor/authenticator/enable</c>.
/// </summary>
/// <param name="SharedKey">The raw shared secret, shown so it can be entered manually.</param>
/// <param name="AuthenticatorUri">
///     The <c>otpauth://</c> URI an authenticator app can scan as a QR code, so the secret need
///     not be typed.
/// </param>
public record AuthenticatorSetupResult(string SharedKey, string AuthenticatorUri);

/// <summary>
///     This class carries one verification code, used to confirm a newly enrolled authenticator
///     and to disable two-factor authentication.
/// </summary>
/// <param name="Code">The time-based (authenticator app) or e-mailed verification code.</param>
public record TwoFactorCodeRequest(string Code);

/// <summary>
///     This class carries the caller's two-factor enrollment state
///     (<c>GET /api/auth/two-factor/status</c>).
/// </summary>
/// <param name="IsEnabled">Whether a second factor is required at sign-in.</param>
/// <param name="HasAuthenticator">
///     Whether an authenticator shared secret has been generated for the account, whether or not
///     two-factor sign-in is required. The UI uses it to say "re-enroll" rather than "enroll".
/// </param>
/// <param name="CanReceiveEmailCode">
///     Whether the account has a confirmed e-mail address that
///     <c>POST /api/auth/two-factor/email/send</c> can deliver a code to.
/// </param>
/// <param name="RecoveryCodesRemaining">
///     How many of the account's single-use recovery codes are unused; zero when none have been
///     generated. The UI should prompt for a new batch when this runs low.
/// </param>
public record TwoFactorStatus(
    bool IsEnabled,
    bool HasAuthenticator,
    bool CanReceiveEmailCode,
    int RecoveryCodesRemaining
);

/// <summary>
///     This class carries a new batch of single-use recovery codes
///     (<c>POST /api/auth/two-factor/recovery-codes/generate</c>, which requires two-factor
///     authentication to be enabled). They are a fallback when the authenticator app or e-mail is
///     unavailable. Like every generated secret (a TSIG key's secret, a webhook's signing
///     secret), they are shown once, in this response; afterwards only
///     <see cref="TwoFactorStatus.RecoveryCodesRemaining" /> reports on them.
/// </summary>
/// <param name="Codes">
///     The 10 new codes, each usable once at <c>POST /api/auth/login/two-factor</c> with
///     <c>"RecoveryCode"</c> as the <see cref="TwoFactorLoginRequest.Provider" />. Generating a
///     batch invalidates every code from earlier batches.
/// </param>
public record RecoveryCodesResult(IReadOnlyList<string> Codes);
