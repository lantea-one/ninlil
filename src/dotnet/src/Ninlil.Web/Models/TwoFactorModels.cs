// Define our namespace.
namespace Ninlil.Web.Models;

/// <summary>
///     This class carries a freshly-generated TOTP (RFC 6238) authenticator enrollment, before
///     the caller has confirmed it by submitting a generated code back to
///     <c>POST /api/auth/two-factor/authenticator/enable</c>.
/// </summary>
/// <param name="SharedKey">The raw shared secret, shown so it can be entered manually.</param>
/// <param name="AuthenticatorUri">
///     The <c>otpauth://</c> URI an authenticator app can scan as a QR code to enroll the same
///     shared secret without it being typed by hand.
/// </param>
public record AuthenticatorSetupResult(string SharedKey, string AuthenticatorUri);

/// <summary>
///     This class carries a single verification code, used both to confirm a newly-enrolled
///     authenticator and to disable two-factor authentication.
/// </summary>
/// <param name="Code">The time-based (authenticator app) or e-mailed verification code.</param>
public record TwoFactorCodeRequest(string Code);

/// <summary>
///     This class carries the current caller's own two-factor enrollment state.
/// </summary>
/// <param name="IsEnabled">Whether a second factor is currently required at sign-in.</param>
/// <param name="HasAuthenticator">
///     Whether an authenticator app shared secret has already been generated for this account
///     (regardless of whether two-factor sign-in is currently required) -- lets the UI offer
///     "re-enroll" rather than "enroll" wording.
/// </param>
/// <param name="CanReceiveEmailCode">
///     Whether this account has a confirmed e-mail address the e-mailed-code action
///     (<c>POST .../two-factor/email/send</c>) could actually deliver a code to.
/// </param>
/// <param name="RecoveryCodesRemaining">
///     How many of this account's own single-use recovery codes have not yet been redeemed. Zero
///     both when two-factor authentication is not enabled at all and when it is enabled but no
///     recovery code batch has ever been generated -- the UI is expected to prompt the caller to
///     generate a batch (or a fresh one) whenever this is low, the same way it would after any
///     other secret nears the end of its usefulness.
/// </param>
public record TwoFactorStatus(
    bool IsEnabled,
    bool HasAuthenticator,
    bool CanReceiveEmailCode,
    int RecoveryCodesRemaining
);

/// <summary>
///     This class carries a freshly-generated batch of single-use two-factor recovery codes, for
///     use as a fallback second factor when an authenticator app or a confirmed e-mail address is
///     unavailable. Exactly like every other secret this project generates (a TSIG key's shared
///     secret, a webhook subscription's own HMAC signing secret), these codes are shown exactly
///     once, in this one response -- <see cref="TwoFactorStatus.RecoveryCodesRemaining" /> is the
///     only thing any later request can ever learn about them again.
/// </summary>
/// <param name="Codes">
///     The 10 freshly-generated codes, each usable exactly once via
///     <c>POST /api/auth/login/two-factor</c> with <c>"RecoveryCode"</c> as its own
///     <see cref="TwoFactorLoginRequest.Provider" />. Generating a new batch immediately
///     invalidates every code from any previous batch, the same "replace wholesale" shape a TSIG
///     key's own secret rotation would have, if one existed.
/// </param>
public record RecoveryCodesResult(IReadOnlyList<string> Codes);
