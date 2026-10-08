using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Token;
using Ninlil.Dns.Server.Cluster.Options;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Examples.Auth;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Services;
using Swashbuckle.AspNetCore.Filters;
using SignInResult = Microsoft.AspNetCore.Identity.SignInResult;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller issues, refreshes and revokes sign-in credentials: a short-lived JSON Web
///     Token access token, a longer-lived opaque refresh token and, for an account with two-factor
///     authentication, the challenge token between the two steps. It also hosts the anonymous
///     password-recovery actions (<see cref="ForgotPassword" /> and <see cref="ResetPassword" />),
///     since their callers are not signed in and every <c>UsersController</c> action needs a
///     sign-in. Every action here allows anonymous callers.
///     <para>
///         <see cref="Login" /> and <see cref="TwoFactorLogin" />, the actions an attacker would
///         use to guess a password or code, are rate limited by the
///         <see cref="ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName" />
///         policy. <see cref="ForgotPassword" />, which sends e-mail to any address an anonymous
///         caller names, has its own counter under the same limits
///         (<see cref="ServiceCollectionAdminLoginRateLimitingExtension.PasswordResetEndpointPolicyName" />).
///         <c>AdminLoginRateLimitOptions</c> explains how that limiter differs from the DNS query
///         rate limiter.
///     </para>
///     <para>
///         <b>Two ways to hold a refresh token.</b> A script or API client gets the refresh token
///         in the response body. The browser admin console asks for
///         <see cref="RefreshTokenDeliveryEnum.Cookie" /> delivery instead: the refresh token then
///         travels only in the HttpOnly cookie of <see cref="RefreshTokenCookie" />, which page
///         scripts cannot read, and <see cref="Refresh" /> and <see cref="Logout" /> use that
///         cookie when the request body names no token. A page reload can then renew the session
///         without exposing the refresh token to JavaScript. "Remember me"
///         (<see cref="LoginRequest.RememberMe" />) chooses between a persistent session and a
///         browser-session one; see <see cref="SessionEntity.IsPersistent" />.
///     </para>
/// </summary>
/// <param name="userManager">Looks up and validates the account signing in.</param>
/// <param name="signInManager">Checks a submitted password against the lockout policy.</param>
/// <param name="jwtTokenService">Issues, validates and revokes access and refresh tokens.</param>
/// <param name="pendingTwoFactor">Tracks a sign-in that is waiting for its second factor.</param>
/// <param name="passwordResetLinkSender">
///     Builds and sends the reset e-mail for <see cref="ForgotPassword" />. The <c>EmailLink</c>
///     mode of <c>UsersController.ResetPassword</c> uses it too.
/// </param>
/// <param name="emailOptions">
///     The current SMTP settings, read on every <see cref="ForgotPassword" /> request so the
///     "can a link be sent" check reflects the latest <c>SmtpSettingsController.Patch</c>.
/// </param>
/// <param name="eventPublisher">
///     Publishes a <see cref="UserAccountChangedEvent" /> after <see cref="ResetPassword" />
///     succeeds, as <c>UsersController</c> does for every account change, so cluster sync
///     replicates the new password.
/// </param>
/// <param name="clusterOptions">
///     Supplies this node's cluster node id, which <see cref="ResetPassword" /> stamps onto
///     <see cref="AuditableEntityBase.OriginatingNodeId" />.
/// </param>
/// <param name="scopeFactory">
///     Creates the service scope <see cref="ForgotPassword" /> sends its e-mail from after the
///     response has gone.
/// </param>
/// <param name="logger">
///     Logs a failed or impossible send in <see cref="ForgotPassword" />, which is never reported
///     to the caller.
/// </param>
[ApiController]
[Route("api/auth")]
public class AuthController(
    UserManager<UserEntity> userManager,
    SignInManager<UserEntity> signInManager,
    IJwtTokenService jwtTokenService,
    PendingTwoFactorSignInCache pendingTwoFactor,
    PasswordResetLinkSender passwordResetLinkSender,
    IOptionsMonitor<EmailSenderOptions> emailOptions,
    IDnsServerEventPublisher eventPublisher,
    IOptionsMonitor<ClusterOptions> clusterOptions,
    IServiceScopeFactory scopeFactory,
    ILogger<AuthController> logger
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the message <see cref="ForgotPassword" /> always returns,
    ///     whatever happened (see that action for why).
    /// </summary>
    private const string ForgotPasswordGenericMessage =
        "If an account exists for that e-mail address, a password reset link has been sent to it.";

    /// <summary>
    ///     This action starts a self-service password reset. Given an e-mail address and no
    ///     sign-in, it sends a reset-link e-mail through <see cref="PasswordResetLinkSender" />
    ///     when a matching account exists, is not disabled, has an e-mail address, and SMTP and
    ///     the public base URL are configured. Allows anonymous callers and is rate limited.
    ///     <para>
    ///         <b>The response never varies.</b> It is always 200 with the same
    ///         <see cref="ForgotPasswordResult" />, whichever condition failed, including "no
    ///         account has this address". An administrator using
    ///         <c>UsersController.ResetPassword</c> may learn whether an account has an e-mail
    ///         address or whether SMTP is configured, but an anonymous caller must not be able to
    ///         tell from the response which addresses have accounts. A delivery failure (the SMTP
    ///         server rejects the message, say) is logged rather than returned as a 500 for the
    ///         same reason.
    ///     </para>
    ///     <para>
    ///         <b>Neither does its timing, as far as is practical.</b> The e-mail is sent in the
    ///         background after the response, so a slow SMTP conversation cannot reveal that an
    ///         account exists. Only the account lookup and the token generation run inline.
    ///     </para>
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(
        ServiceCollectionAdminLoginRateLimitingExtension.PasswordResetEndpointPolicyName
    )]
    [SwaggerRequestExample(typeof(ForgotPasswordRequest), typeof(ForgotPasswordRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForgotPasswordResultExample))]
    public async Task<ActionResult<ForgotPasswordResult>> ForgotPassword(
        ForgotPasswordRequest request
    )
    {
        UserEntity user = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : await userManager.FindByEmailAsync(request.Email);

        bool canSendLink =
            user is not null
            && !user.IsDisabled
            && !string.IsNullOrWhiteSpace(user.Email)
            && !string.IsNullOrWhiteSpace(emailOptions.CurrentValue.SmtpHost);

        if (canSendLink && !passwordResetLinkSender.IsPublicBaseUrlConfigured)
        {
            // The caller is not told (see the summary); the log is where an administrator finds
            // out why no e-mail arrived.
            logger.LogWarning(
                "A Forgot Password reset link for account {UserId} was not sent: "
                    + PasswordResetLinkSender.PublicBaseUrlNotConfiguredMessage,
                user.Id
            );
            canSendLink = false;
        }

        if (canSendLink)
        {
            try
            {
                string resetUrl = await passwordResetLinkSender.CreateResetLinkAsync(user);

                // Deliberately not awaited: the response must not wait for SMTP (see the summary).
                _ = DeliverResetLinkAsync(user.Id, user.Email, resetUrl);
            }
            catch (Exception exception)
            {
                // A 500 here would only ever happen for an existing account, so it is logged and
                // the response stays the same (see the summary).
                logger.LogWarning(
                    exception,
                    "Failed to build a Forgot Password reset link for account {UserId}.",
                    user.Id
                );
            }
        }

        return Ok(new ForgotPasswordResult(ForgotPasswordGenericMessage));
    }

    /// <summary>
    ///     This method sends a Forgot Password e-mail from a service scope of its own, since it
    ///     runs on after the request (and its scope) has ended. It never throws: a failure is
    ///     logged so an administrator can still find a broken SMTP configuration.
    /// </summary>
    private async Task DeliverResetLinkAsync(Guid userId, string toAddress, string resetUrl)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IEmailSender emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

            await PasswordResetLinkSender.SendLinkAsync(
                emailSender,
                toAddress,
                resetUrl,
                CancellationToken.None
            );
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Failed to send a Forgot Password reset link to account {UserId}.",
                userId
            );
        }
    }

    /// <summary>
    ///     This action checks a username and password. It returns the tokens of a completed sign-in
    ///     or, for an account with two-factor authentication, a challenge token to submit to
    ///     <see cref="TwoFactorLogin" />. Returns 401 for a wrong password or a disabled or unknown
    ///     account and 423 while the account is locked out. Allows anonymous callers and is rate
    ///     limited.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(
        ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName
    )]
    [SwaggerRequestExample(typeof(LoginRequest), typeof(LoginRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(LoginResponseExample))]
    public async Task<ActionResult<LoginResponse>> Login(
        LoginRequest request,
        CancellationToken stoppingToken
    )
    {
        UserEntity user = await userManager.FindByNameAsync(request.UserName);
        if (user is null || user.IsDisabled)
            return Unauthorized();

        SignInResult result = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true
        );

        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked);

        if (!result.Succeeded)
            return Unauthorized();

        // CheckPasswordSignInAsync never sets SignInResult.RequiresTwoFactor; only
        // PasswordSignInAsync does, and that also writes an "Identity.Application" cookie for a
        // scheme this JWT-only API does not register. This checks the same condition
        // SignInManager's protected IsTwoFactorEnabledAsync uses, without that side effect.
        bool requiresTwoFactor =
            await userManager.GetTwoFactorEnabledAsync(user)
            && (await userManager.GetValidTwoFactorProvidersAsync(user)).Count > 0;

        if (requiresTwoFactor)
        {
            string mfaToken = pendingTwoFactor.Create(user.Id);
            return Ok(new LoginResponse(true, mfaToken, null, null, null));
        }

        return Ok(
            await IssueLoginResponseAsync(
                user,
                request.RefreshTokenDelivery,
                request.RememberMe,
                stoppingToken
            )
        );
    }

    /// <summary>
    ///     This action completes a sign-in that <see cref="Login" /> marked as needing a second
    ///     factor, checking an authenticator code, an e-mailed code or a recovery code against the
    ///     pending sign-in's account. Each challenge token can be used once. Returns 401 for an
    ///     unknown or used challenge token or a wrong code. Allows anonymous callers and is rate
    ///     limited.
    /// </summary>
    [HttpPost("login/two-factor")]
    [AllowAnonymous]
    [EnableRateLimiting(
        ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName
    )]
    [SwaggerRequestExample(typeof(TwoFactorLoginRequest), typeof(TwoFactorLoginRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(LoginResponseExample))]
    public async Task<ActionResult<LoginResponse>> TwoFactorLogin(
        TwoFactorLoginRequest request,
        CancellationToken stoppingToken
    )
    {
        Guid? userId = pendingTwoFactor.Consume(request.MfaToken);
        if (userId is null)
            return Unauthorized();

        UserEntity user = await userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null || user.IsDisabled)
            return Unauthorized();

        // A recovery code is redeemed (checked and, on success, invalidated in one call). Neither
        // token provider could verify it, because it is not a TOTP or e-mail token.
        if (string.Equals(request.Provider, "RecoveryCode", StringComparison.OrdinalIgnoreCase))
        {
            IdentityResult redeemResult = await userManager.RedeemTwoFactorRecoveryCodeAsync(
                user,
                request.Code
            );
            if (!redeemResult.Succeeded)
                return Unauthorized();

            return Ok(
                await IssueLoginResponseAsync(
                    user,
                    request.RefreshTokenDelivery,
                    request.RememberMe,
                    stoppingToken
                )
            );
        }

        string provider = string.Equals(
            request.Provider,
            "Email",
            StringComparison.OrdinalIgnoreCase
        )
            ? TokenOptions.DefaultEmailProvider
            : TokenOptions.DefaultAuthenticatorProvider;

        bool isCodeValid = await userManager.VerifyTwoFactorTokenAsync(
            user,
            provider,
            request.Code
        );
        if (!isCodeValid)
            return Unauthorized();

        return Ok(
            await IssueLoginResponseAsync(
                user,
                request.RefreshTokenDelivery,
                request.RememberMe,
                stoppingToken
            )
        );
    }

    /// <summary>
    ///     This action exchanges a valid refresh token for a new access and refresh token pair and
    ///     revokes the old refresh token (rotation), so it cannot be reused. The token comes from
    ///     the request body when one is given there, otherwise from the browser's refresh cookie,
    ///     in which case the new refresh token goes back as a cookie rather than in the body. The
    ///     new session keeps the old one's <see cref="SessionEntity.IsPersistent" /> choice.
    ///     Returns 401 for an invalid, expired or revoked token or a disabled account. Allows
    ///     anonymous callers.
    ///     <para>
    ///         A failed cookie exchange leaves the cookie alone. Two tabs reloading together both
    ///         send the same cookie; the first rotates it and the second fails. If that failure
    ///         cleared the cookie, its response would erase the new cookie the first tab just got
    ///         and sign both out. Left alone, the second tab retries with the rotated cookie. A
    ///         dead cookie costs only a 401, and <see cref="Logout" /> clears it.
    ///     </para>
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [SwaggerRequestExample(typeof(RefreshRequest), typeof(RefreshRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AuthenticationResultExample))]
    public async Task<ActionResult<AuthenticationResult>> Refresh(
        RefreshRequest request,
        CancellationToken stoppingToken
    )
    {
        bool fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
        string rawRefreshToken = fromCookie
            ? RefreshTokenCookie.Read(HttpContext)
            : request.RefreshToken;

        SessionEntity session = await jwtTokenService.ValidateRefreshTokenAsync(
            rawRefreshToken,
            stoppingToken
        );
        UserEntity user = session is null
            ? null
            : await userManager.FindByIdAsync(session.UserId.ToString());

        if (user is null || user.IsDisabled)
            return Unauthorized();

        await jwtTokenService.RevokeRefreshTokenAsync(session, stoppingToken);

        LoginResponse response = await IssueTokensAsync(
            user,
            fromCookie ? RefreshTokenDeliveryEnum.Cookie : RefreshTokenDeliveryEnum.Body,
            session.IsPersistent,
            stoppingToken
        );

        return Ok(
            new AuthenticationResult(
                response.AccessToken,
                response.ExpiresAtUtc!.Value,
                response.RefreshToken
            )
        );
    }

    /// <summary>
    ///     This action revokes a refresh token, the one named in the body or otherwise the
    ///     browser's refresh cookie, which is then cleared. Holding the raw refresh token is enough
    ///     to revoke it, so no access token is needed. Always returns 204, even for an unknown
    ///     token. Allows anonymous callers.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [SwaggerRequestExample(typeof(RefreshRequest), typeof(RefreshRequestExample))]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken stoppingToken)
    {
        bool fromCookie = string.IsNullOrEmpty(request?.RefreshToken);
        string rawRefreshToken = fromCookie
            ? RefreshTokenCookie.Read(HttpContext)
            : request.RefreshToken;

        SessionEntity session = await jwtTokenService.ValidateRefreshTokenAsync(
            rawRefreshToken,
            stoppingToken
        );

        if (session is not null)
            await jwtTokenService.RevokeRefreshTokenAsync(session, stoppingToken);

        if (fromCookie)
            RefreshTokenCookie.Clear(HttpContext);

        return NoContent();
    }

    /// <summary>
    ///     This action sets a new password from a password-reset token. It is the second step of
    ///     both <see cref="ForgotPassword" /> and the <c>EmailLink</c> mode of
    ///     <c>UsersController.ResetPassword</c>, called from the admin console's "set new password"
    ///     page. The caller is not signed in, so it allows anonymous callers;
    ///     <see cref="ConfirmPasswordResetRequest.Token" /> is the only proof of authorization, and
    ///     <see cref="UserManager{TUser}.ResetPasswordAsync" /> rejects a wrong, expired or used
    ///     token. An unknown account and an undecodable token return the same 400 message, so the
    ///     response does not reveal whether an account exists.
    /// </summary>
    [HttpPost("reset-password")]
    [AllowAnonymous]
    [SwaggerRequestExample(
        typeof(ConfirmPasswordResetRequest),
        typeof(ConfirmPasswordResetRequestExample)
    )]
    public async Task<IActionResult> ResetPassword(ConfirmPasswordResetRequest request)
    {
        UserEntity user = await userManager.FindByIdAsync(request.UserId.ToString());

        string rawToken;
        try
        {
            rawToken = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        }
        catch (FormatException)
        {
            throw new ValidationException("This password reset link is no longer valid.");
        }

        if (user is null)
            throw new ValidationException("This password reset link is no longer valid.");

        IdentityResult result = await userManager.ResetPasswordAsync(
            user,
            rawToken,
            request.NewPassword
        );
        result.ThrowIfFailed();

        user.OriginatingNodeId = clusterOptions.CurrentValue.NodeId;
        await userManager.UpdateAsync(user);

        eventPublisher.Publish(new UserAccountChangedEvent(DateTimeOffset.UtcNow, user.Id));

        return NoContent();
    }

    /// <summary>
    ///     This method issues tokens for a newly signed-in account (<see cref="Login" /> or
    ///     <see cref="TwoFactorLogin" />). With cookie delivery the session is persistent only when
    ///     "Remember me" was checked; with body delivery it is always persistent.
    /// </summary>
    private Task<LoginResponse> IssueLoginResponseAsync(
        UserEntity user,
        RefreshTokenDeliveryEnum delivery,
        bool rememberMe,
        CancellationToken stoppingToken
    ) =>
        IssueTokensAsync(
            user,
            delivery,
            delivery == RefreshTokenDeliveryEnum.Body || rememberMe,
            stoppingToken
        );

    /// <summary>
    ///     This method issues an access and refresh token pair and builds the response every
    ///     completed sign-in returns. With cookie delivery the refresh token is written only to the
    ///     <see cref="RefreshTokenCookie" /> cookie and the body's refresh-token field is
    ///     <see langword="null" />.
    /// </summary>
    private async Task<LoginResponse> IssueTokensAsync(
        UserEntity user,
        RefreshTokenDeliveryEnum delivery,
        bool isPersistent,
        CancellationToken stoppingToken
    )
    {
        AccessTokenResult accessToken = await jwtTokenService.CreateAccessTokenAsync(
            user,
            stoppingToken
        );
        RefreshTokenResult refreshToken = await jwtTokenService.CreateRefreshTokenAsync(
            user.Id,
            HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            Request.Headers.UserAgent.ToString(),
            isPersistent,
            stoppingToken
        );

        bool asCookie = delivery == RefreshTokenDeliveryEnum.Cookie;
        if (asCookie)
            RefreshTokenCookie.Write(HttpContext, refreshToken.RefreshToken, refreshToken.Session);

        return new LoginResponse(
            false,
            null,
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            asCookie ? null : refreshToken.RefreshToken
        );
    }
}
