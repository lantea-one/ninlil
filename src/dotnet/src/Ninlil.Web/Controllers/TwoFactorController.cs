using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.TwoFactor;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller manages the currently signed-in caller's own two-factor authentication
///     enrollment: enrolling and confirming a TOTP (RFC 6238) authenticator, disabling two-factor
///     authentication, and requesting an e-mailed verification code. Verifying a code as part of
///     signing in itself is <see cref="AuthController.TwoFactorLogin" />'s own job, not this
///     controller's -- these actions all act on an already-authenticated caller.
/// </summary>
/// <param name="userManager">Reads and updates the current caller's own two-factor settings.</param>
/// <param name="emailSender">Delivers an e-mailed verification code.</param>
/// <param name="eventPublisher">
///     The event bus every mutating action here publishes a <see cref="UserAccountChangedEvent" />
///     through, so the cluster's own identity/RBAC federation re-syncs this account's own
///     TOTP secret/recovery codes (re-encrypted under each peer's own key ring) the moment either
///     one changes -- see <see cref="UsersController" />'s own doc comment for the same pattern.
/// </param>
[ApiController]
[Route("api/auth/two-factor")]
[Authorize]
public class TwoFactorController(
    UserManager<UserEntity> userManager,
    IEmailSender emailSender,
    IDnsServerEventPublisher eventPublisher
) : ControllerBase
{
    /// <summary>
    ///     This action reports the current caller's own two-factor enrollment state -- whether
    ///     two-factor sign-in is currently required, and whether an authenticator app has already
    ///     been confirmed. Built for the self-service account-security page, which has
    ///     no other way to learn this: <c>UserClaimsBuilder</c> never places a two-factor claim on
    ///     an issued access token (a stale, cached "enabled" claim on an already-issued token
    ///     would be actively misleading the moment it changed), and the only other place this
    ///     state is exposed at all, <c>UsersController</c>'s own <c>UserSummary</c>, requires the
    ///     <c>users:read</c> permission an ordinary, non-administrative account has no reason to
    ///     hold.
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
    ///     This action generates a fresh TOTP shared secret for the current caller, replacing any
    ///     previously-generated (and not yet confirmed) one. Two-factor authentication is not yet
    ///     required at this point -- <see cref="EnableAuthenticator" /> must still confirm it.
    /// </summary>
    [HttpPost("authenticator/setup")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AuthenticatorSetupResultExample))]
    public async Task<ActionResult<AuthenticatorSetupResult>> SetUpAuthenticator()
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        await userManager.ResetAuthenticatorKeyAsync(user);
        string sharedKey = await userManager.GetAuthenticatorKeyAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        string authenticatorUri =
            $"otpauth://totp/{Uri.EscapeDataString("Ninlil.Dns")}:{Uri.EscapeDataString(user.UserName)}"
            + $"?secret={sharedKey}&issuer={Uri.EscapeDataString("Ninlil.Dns")}&digits=6";

        return Ok(new AuthenticatorSetupResult(sharedKey, authenticatorUri));
    }

    /// <summary>
    ///     This action confirms a TOTP enrollment by checking a freshly-generated code, and only
    ///     then turns on the requirement to supply a second factor at sign-in.
    /// </summary>
    [HttpPost("authenticator/enable")]
    [SwaggerRequestExample(typeof(TwoFactorCodeRequest), typeof(TwoFactorCodeRequestExample))]
    public async Task<IActionResult> EnableAuthenticator(TwoFactorCodeRequest request)
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        bool isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            request.Code
        );

        if (!isCodeValid)
            throw new ValidationException("The provided authenticator code is not valid.");

        await userManager.SetTwoFactorEnabledAsync(user, true);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action turns off the requirement to supply a second factor at sign-in, after
    ///     confirming the caller can still produce a valid code -- so a stolen access token alone
    ///     is not enough to disable this protection.
    /// </summary>
    [HttpPost("disable")]
    [SwaggerRequestExample(typeof(TwoFactorCodeRequest), typeof(TwoFactorCodeRequestExample))]
    public async Task<IActionResult> Disable(TwoFactorCodeRequest request)
    {
        UserEntity user = await GetCurrentUserAsync();
        if (user is null)
            return Unauthorized();

        bool isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            request.Code
        );

        if (!isCodeValid)
            throw new ValidationException("The provided authenticator code is not valid.");

        await userManager.SetTwoFactorEnabledAsync(user, false);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This action e-mails the current caller a one-time verification code, for use as a
    ///     second factor at sign-in when an authenticator app is not available. The caller's
    ///     e-mail address must already be confirmed.
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
    ///     This action generates a fresh batch of 10 single-use two-factor recovery codes for the
    ///     current caller, replacing (invalidating) any previously-generated batch entirely. Two-
    ///     factor authentication must already be enabled -- a recovery code exists purely as a
    ///     fallback for a second factor this account does not otherwise require, so generating one
    ///     first would have nothing to be a fallback *for*, mirroring the guard
    ///     <see cref="EnableAuthenticator" /> itself already establishes for a similar reason.
    /// </summary>
    [HttpPost("recovery-codes/generate")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RecoveryCodesResultExample))]
    public async Task<ActionResult<RecoveryCodesResult>> GenerateRecoveryCodes()
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

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return Ok(new RecoveryCodesResult([.. codes]));
    }

    /// <summary>
    ///     This constant is how many recovery codes <see cref="GenerateRecoveryCodes" /> generates
    ///     per batch -- ASP.NET Core Identity's own conventional default for this feature.
    /// </summary>
    private const int RecoveryCodeCount = 10;

    /// <summary>
    ///     This method resolves the currently authenticated caller's own <see cref="UserEntity" />
    ///     from the "sub" claim on their access token, or <see langword="null" /> if no such user
    ///     exists (or none is authenticated at all).
    /// </summary>
    private async Task<UserEntity> GetCurrentUserAsync()
    {
        Guid? userId = User.GetUserId();

        return userId is null ? null : await userManager.FindByIdAsync(userId.Value.ToString());
    }
}
