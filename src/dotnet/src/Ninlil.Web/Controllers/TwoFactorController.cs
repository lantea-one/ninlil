using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.TwoFactor;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the signed-in caller's two-factor authentication: setting up and
///     confirming a TOTP (RFC 6238) authenticator, turning two-factor sign-in off, generating
///     recovery codes and requesting an e-mailed code. Every action needs only a signed-in caller
///     and acts on that caller's account. Checking a code during sign-in is the job of
///     <see cref="AuthController.TwoFactorLogin" />.
/// </summary>
/// <param name="userManager">Reads and updates the caller's two-factor settings.</param>
/// <param name="emailSender">Delivers an e-mailed verification code.</param>
/// <param name="eventPublisher">
///     Publishes a <see cref="UserAccountChangedEvent" /> after every change, so cluster sync
///     copies the account's TOTP secret and recovery codes to the other nodes (re-encrypted under
///     each node's key ring), as <see cref="UsersController" /> does for other account changes.
/// </param>
/// <param name="attemptLimiter">
///     Limits wrong authenticator codes per user, so a stolen access token cannot be used to
///     guess the code.
/// </param>
/// <param name="emailCodeCooldown">
///     Limits how often an e-mailed code can be requested per user, shared with the sign-in step,
///     so a stolen access token cannot be used to flood the owner's inbox.
/// </param>
/// <param name="userStore">
///     The user store, used to write a new authenticator key without the security-stamp change
///     that <see cref="UserManager{TUser}.ResetAuthenticatorKeyAsync" /> makes (see
///     <see cref="SetUpAuthenticator" />).
/// </param>
/// <param name="jwtTokenService">
///     Revokes the account's refresh tokens when two-factor settings change, so a stolen session
///     cannot refresh past the change.
/// </param>
[ApiController]
[Route("api/auth/two-factor")]
[Authorize]
public class TwoFactorController(
    UserManager<UserEntity> userManager,
    IEmailSender emailSender,
    IDnsServerEventPublisher eventPublisher,
    TwoFactorCodeAttemptLimiter attemptLimiter,
    TwoFactorEmailCodeCooldown emailCodeCooldown,
    IUserStore<UserEntity> userStore,
    IJwtTokenService jwtTokenService
) : ControllerBase
{
    /// <summary>
    ///     This action reports the caller's two-factor state: whether two-factor sign-in is
    ///     required, whether an authenticator key exists, whether the account has a confirmed
    ///     e-mail address, and how many recovery codes remain. The account-security page needs this
    ///     endpoint because access tokens carry no two-factor claim (it would go stale as soon as
    ///     the setting changed), and <c>UsersController</c> exposes the state only to holders of
    ///     <c>users:read</c>.
    /// </summary>
    [HttpGet("status")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(TwoFactorStatusExample))]
    public async Task<ActionResult<TwoFactorStatus>> GetStatus()
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        string existingKey = await userManager.GetAuthenticatorKeyAsync(user);
        int recoveryCodesRemaining = await userManager.CountRecoveryCodesAsync(user);

        return Ok(
            new TwoFactorStatus(
                user.TwoFactorEnabled,
                !string.IsNullOrEmpty(existingKey),
                user.EmailConfirmed && !string.IsNullOrWhiteSpace(user.Email),
                recoveryCodesRemaining
            )
        );
    }

    /// <summary>
    ///     This action generates a new TOTP shared secret for the caller and returns it with an
    ///     <c>otpauth://</c> URI for an authenticator app. <see cref="EnableAuthenticator" /> must
    ///     confirm it before two-factor sign-in becomes required. This call does not end the
    ///     caller's session: the confirmation that follows needs it, and
    ///     <see cref="EnableAuthenticator" /> ends every session of the account.
    ///     <para>
    ///         While two-factor sign-in is on with an authenticator secret, the request must carry
    ///         a valid current authenticator code or an unused recovery code (which this call
    ///         uses up) in the body; otherwise this returns 400 and the secret is unchanged. After
    ///         5 wrong codes in 15 minutes (shared with the other code checks of this controller
    ///         and the sign-in) it returns 429 with a <c>Retry-After</c> header, without checking
    ///         the code. A
    ///         stolen access token alone then cannot swap in an attacker's secret. First-time
    ///         setup, or setup while two-factor sign-in is off, needs no body.
    ///     </para>
    /// </summary>
    [HttpPost("authenticator/setup")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AuthenticatorSetupResultExample))]
    public async Task<ActionResult<AuthenticatorSetupResult>> SetUpAuthenticator(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] TwoFactorCodeRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (
            user.TwoFactorEnabled
            && !string.IsNullOrEmpty(await userManager.GetAuthenticatorKeyAsync(user))
        )
        {
            if (attemptLimiter.IsBlocked(user.Id, out TimeSpan retryAfter))
                return TwoFactorCodeAttemptLimiter.TooManyRequests(this, retryAfter);

            // The caller proves control of the current second factor before it is replaced.
            string code = request?.Code;
            bool isCurrentCodeValid =
                !string.IsNullOrWhiteSpace(code)
                && (
                    await userManager.VerifyTwoFactorTokenAsync(
                        user,
                        TokenOptions.DefaultAuthenticatorProvider,
                        code
                    ) || (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
                );

            if (!isCurrentCodeValid)
            {
                attemptLimiter.RecordFailure(user.Id);
                throw new ValidationException(
                    "Replacing an active authenticator needs a valid current authenticator code "
                        + "or a recovery code."
                );
            }

            attemptLimiter.RecordSuccess(user.Id);
        }

        // UserManager.ResetAuthenticatorKeyAsync would also replace the security stamp and end the
        // access token this caller needs for the confirmation step. The key is written through
        // the store instead; EnableAuthenticator ends the sessions when the key is confirmed.
        await ((IUserAuthenticatorKeyStore<UserEntity>)userStore).SetAuthenticatorKeyAsync(
            user,
            userManager.GenerateNewAuthenticatorKey(),
            stoppingToken
        );
        IdentityResult updateResult = await userManager.UpdateAsync(user);
        updateResult.ThrowIfFailed();
        string sharedKey = await userManager.GetAuthenticatorKeyAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        string authenticatorUri =
            $"otpauth://totp/{Uri.EscapeDataString("Ninlil.Dns")}:{Uri.EscapeDataString(user.UserName)}"
            + $"?secret={sharedKey}&issuer={Uri.EscapeDataString("Ninlil.Dns")}&digits=6";

        return Ok(new AuthenticatorSetupResult(sharedKey, authenticatorUri));
    }

    /// <summary>
    ///     This action confirms authenticator setup by checking a code from the app, and only then
    ///     makes a second factor required at sign-in. Returns 400 for a wrong code and 429, with a
    ///     <c>Retry-After</c> header, after 5 wrong codes in 15 minutes. A successful call
    ///     replaces the account's security stamp and revokes every refresh token of the account,
    ///     including the caller's own session: every access token issued before it, the caller's
    ///     included, answers 401 on its next request, and the caller signs in again with the
    ///     second factor.
    /// </summary>
    [HttpPost("authenticator/enable")]
    [SwaggerRequestExample(typeof(TwoFactorCodeRequest), typeof(TwoFactorCodeRequestExample))]
    public async Task<IActionResult> EnableAuthenticator(
        TwoFactorCodeRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (attemptLimiter.IsBlocked(user.Id, out TimeSpan retryAfter))
            return TwoFactorCodeAttemptLimiter.TooManyRequests(this, retryAfter);

        bool isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            request.Code
        );

        if (!isCodeValid)
        {
            attemptLimiter.RecordFailure(user.Id);
            throw new ValidationException("The provided authenticator code is not valid.");
        }

        attemptLimiter.RecordSuccess(user.Id);

        IdentityResult enableResult = await userManager.SetTwoFactorEnabledAsync(user, true);
        enableResult.ThrowIfFailed();
        await jwtTokenService.RevokeAllRefreshTokensAsync(user.Id, stoppingToken);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action stops requiring a second factor at sign-in. It needs a valid authenticator
    ///     code as well as the access token. Returns 400 for a wrong code and 429, with a
    ///     <c>Retry-After</c> header, after 5 wrong codes in 15 minutes. A successful call ends
    ///     every session of the account in the same way as <see cref="EnableAuthenticator" />.
    /// </summary>
    [HttpPost("disable")]
    [SwaggerRequestExample(typeof(TwoFactorCodeRequest), typeof(TwoFactorCodeRequestExample))]
    public async Task<IActionResult> Disable(
        TwoFactorCodeRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (attemptLimiter.IsBlocked(user.Id, out TimeSpan retryAfter))
            return TwoFactorCodeAttemptLimiter.TooManyRequests(this, retryAfter);

        bool isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            request.Code
        );

        if (!isCodeValid)
        {
            attemptLimiter.RecordFailure(user.Id);
            throw new ValidationException("The provided authenticator code is not valid.");
        }

        attemptLimiter.RecordSuccess(user.Id);

        IdentityResult disableResult = await userManager.SetTwoFactorEnabledAsync(user, false);
        disableResult.ThrowIfFailed();
        await jwtTokenService.RevokeAllRefreshTokensAsync(user.Id, stoppingToken);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action e-mails the caller a short-lived verification code from the e-mail token
    ///     provider and returns 202. The account must have a confirmed e-mail address, otherwise
    ///     this returns 400. Returns 429 with a <c>Retry-After</c> header when a code was sent to
    ///     the account in the last 60 seconds, by this action or by the sign-in step. Because it needs a signed-in caller, it cannot be called during the
    ///     two-factor step of a sign-in; <see cref="AuthController.SendTwoFactorEmailCode" /> does
    ///     that.
    /// </summary>
    [HttpPost("email/send")]
    public async Task<IActionResult> SendEmailCode()
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (!user.EmailConfirmed || string.IsNullOrWhiteSpace(user.Email))
            throw new ValidationException(
                "This account has no confirmed e-mail address to send a code to."
            );

        if (!emailCodeCooldown.TryAcquire(user.Id, null, out TimeSpan retryAfter))
            return TwoFactorCodeAttemptLimiter.TooManyRequests(
                this,
                retryAfter,
                "A code was sent recently",
                "A code was sent recently."
            );

        string code = await userManager.GenerateTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultEmailProvider
        );

        await emailSender.SendAsync(
            user.Email,
            "Your Ninlil sign-in code",
            $"<p>Your verification code is <strong>{code}</strong>. It expires shortly.</p>"
        );

        return Accepted();
    }

    /// <summary>
    ///     This action generates a new batch of single-use recovery codes for the caller and
    ///     invalidates any earlier batch. A recovery code is a fallback for the second factor, so
    ///     two-factor sign-in must already be enabled; otherwise this returns 400. A new batch
    ///     replaces the account's security stamp and revokes every refresh token of the account,
    ///     so the caller's own access token answers 401 on its next request: the caller reads the
    ///     codes from this response and signs in again.
    /// </summary>
    [HttpPost("recovery-codes/generate")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecoveryCodesResultExample))]
    public async Task<ActionResult<RecoveryCodesResult>> GenerateRecoveryCodes(
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        if (!user.TwoFactorEnabled)
            throw new ValidationException(
                "Two-factor authentication must already be enabled before recovery codes can be generated."
            );

        IEnumerable<string> codes = await userManager.GenerateNewTwoFactorRecoveryCodesAsync(
            user,
            RecoveryCodeCount
        );

        // Identity does not replace the stamp for a new batch of recovery codes; a batch is a
        // two-factor change, so it ends the sessions like the other changes do.
        IdentityResult stampResult = await userManager.UpdateSecurityStampAsync(user);
        stampResult.ThrowIfFailed();
        await jwtTokenService.RevokeAllRefreshTokensAsync(user.Id, stoppingToken);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return Ok(new RecoveryCodesResult([.. codes]));
    }

    /// <summary>
    ///     This constant contains how many recovery codes <see cref="GenerateRecoveryCodes" />
    ///     generates per batch, the usual ASP.NET Core Identity default.
    /// </summary>
    private const int RecoveryCodeCount = 10;

    /// <summary>
    ///     This method loads the signed-in caller's <see cref="UserEntity" /> from the user id
    ///     claim on the access token, or returns <see langword="null" /> when there is none.
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }
}
