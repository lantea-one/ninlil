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
///     This controller issues, refreshes, and revokes the sign-in credentials every other
///     authenticated endpoint in this project depends on: a short-lived JSON Web Token access
///     token, a longer-lived opaque refresh token, and (for an account with two-factor
///     authentication enabled) the intermediate challenge between them. It also hosts the two
///     genuinely anonymous, self-service password-recovery actions (<see cref="ForgotPassword" />/
///     <see cref="ResetPassword" />) -- neither caller is signed in at either step, so both live
///     here rather than on <c>UsersController</c>, which gates every action behind either
///     <see cref="AuthorizeAttribute" /> or a specific administrative permission.
///     <para>
///         <see cref="Login" /> and <see cref="TwoFactorLogin" /> -- the two actions an attacker
///         could otherwise hammer to guess a credential or second-factor code -- each carry
///         <see cref="EnableRateLimitingAttribute" />, scoped to
///         <see cref="ServiceCollectionAdminLoginRateLimitingExtension.AuthenticationEndpointPolicyName" />.
///         See <c>Identity.Options.AdminLoginRateLimitOptions</c>'s own doc comment for why this
///         limiter is a genuinely separate mechanism from, and deliberately has the opposite
///         failure posture to, <c>Server.RateLimiting</c>'s own DNS-query rate limiter.
///     </para>
///     <para>
///         <b>Two ways to hold a refresh token.</b> A script or API client asks for nothing special
///         and gets the refresh token in the response body, exactly as always. The browser admin
///         console asks for <see cref="RefreshTokenDeliveryEnum.Cookie" /> delivery instead: the
///         refresh token then travels only in <see cref="RefreshTokenCookie" />'s HttpOnly cookie,
///         never in a body page script could read, and <see cref="Refresh" />/<see cref="Logout" />
///         act on that cookie whenever the request body names no token. This is what lets a page
///         reload renew the session without the refresh token ever being exposed to JavaScript.
///         "Remember me" (<see cref="LoginRequest.RememberMe" />) chooses between a persistent
///         session and a browser-session one; see <see cref="SessionEntity.IsPersistent" />.
///     </para>
/// </summary>
/// <param name="userManager">Looks up and validates the account signing in.</param>
/// <param name="signInManager">Checks a submitted password against lockout/2FA policy.</param>
/// <param name="jwtTokenService">Issues, validates, and revokes access/refresh tokens.</param>
/// <param name="pendingTwoFactor">Tracks a sign-in still waiting on its second factor.</param>
/// <param name="passwordResetLinkSender">
///     Builds and sends the actual reset e-mail <see cref="ForgotPassword" /> triggers, once it has
///     already decided a link should go out -- shared with the administrator-driven
///     <c>UsersController.ResetPassword</c>'s own <c>EmailLink</c> delivery mode.
/// </param>
/// <param name="emailOptions">
///     The live, currently-effective SMTP server settings -- read fresh on every
///     <see cref="ForgotPassword" /> request so its own pre-flight "can a link even be sent"
///     check always reflects the most recently saved <c>SmtpSettingsController.Patch</c>, matching
///     <c>UsersController.ResetPassword</c>'s own identical reasoning.
/// </param>
/// <param name="eventPublisher">
///     The event bus <see cref="ResetPassword" /> publishes a <see cref="UserAccountChangedEvent" />
///     through after a successful anonymous, link-based password reset -- the cluster's own
///     identity/RBAC federation is its only real subscriber, mirroring
///     <c>UsersController</c>'s own identical publish-on-every-mutation convention so this genuinely
///     anonymous reset path is not a silent gap in what actually syncs cluster-wide.
/// </param>
/// <param name="clusterOptions">
///     Read for this node's own stable cluster node id, stamped onto
///     <see cref="AuditableEntityBase.OriginatingNodeId" /> by <see cref="ResetPassword" /> -- mirrors
///     <c>UsersController</c>'s own identical convention.
/// </param>
/// <param name="logger">
///     Logs a real send failure inside <see cref="ForgotPassword" /> without ever surfacing it to
///     the caller -- see that action's own doc comment for why a delivery failure must stay
///     server-side only.
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
    ILogger<AuthController> logger
) : ControllerBase
{
    /// <summary>
    ///     This constant contains the one, identical message <see cref="ForgotPassword" /> returns
    ///     regardless of whether the submitted address actually has an account, is disabled, has no
    ///     e-mail address on file, or SMTP is not configured at all -- see that action's own doc
    ///     comment for why this must never vary.
    /// </summary>
    private const string ForgotPasswordGenericMessage =
        "If an account exists for that e-mail address, a password reset link has been sent to it.";

    /// <summary>
    ///     This action starts a genuinely self-service password reset: given only an e-mail
    ///     address, with no sign-in of any kind, it looks up the matching account and -- if one
    ///     exists, is not disabled, has an e-mail address on file, and this server has SMTP
    ///     configured -- sends it a real reset-link e-mail via the exact same
    ///     <see cref="PasswordResetLinkSender" /> round trip <c>UsersController.ResetPassword</c>'s
    ///     own <c>EmailLink</c> delivery mode uses.
    ///     <para>
    ///         <b>Security-critical, deliberate departure from that admin-driven counterpart</b>:
    ///         this action's response is always the exact same <see cref="ForgotPasswordResult" />,
    ///         with the exact same <c>200</c> status, no matter which of those conditions failed --
    ///         including the "no account with this address exists at all" case. An authenticated
    ///         administrator resetting a known account's password may legitimately learn whether
    ///         that specific account has an e-mail address or whether SMTP is configured; a
    ///         completely anonymous caller submitting an arbitrary address must never be able to
    ///         use response shape, status code, or timing to enumerate which e-mail addresses have
    ///         accounts on this server -- a classic account-enumeration risk for exactly this kind
    ///         of endpoint. A real delivery-time failure (e.g. the SMTP server rejects the message)
    ///         is caught and logged here rather than allowed to surface as a <c>500</c>, for the
    ///         same reason -- an attacker distinguishing "delivery attempted and failed" from
    ///         "delivery never attempted" is exactly as much of a leak as distinguishing "sent" from
    ///         "not sent" would be.
    ///     </para>
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [SwaggerRequestExample(typeof(ForgotPasswordRequest), typeof(ForgotPasswordRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(ForgotPasswordResultExample))]
    public async Task<ActionResult<ForgotPasswordResult>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken stoppingToken
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

        if (canSendLink)
        {
            try
            {
                await passwordResetLinkSender.SendAsync(
                    user,
                    Request.Scheme,
                    Request.Host,
                    stoppingToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A real delivery-time failure (SMTP host unreachable, rejected message, etc.)
                // must never surface to the caller -- see this action's own doc comment on why
                // the response is identical no matter what happened server-side. Logged so an
                // administrator can still discover a genuinely broken SMTP configuration through
                // this server's own logs, just never through this endpoint's response.
                logger.LogWarning(
                    exception,
                    "Failed to send a Forgot Password reset link to account {UserId}.",
                    user.Id
                );
            }
        }

        return Ok(new ForgotPasswordResult(ForgotPasswordGenericMessage));
    }

    /// <summary>
    ///     This action checks a username and password, returning either a completed sign-in's
    ///     tokens or, for an account with two-factor authentication enabled, a pending challenge
    ///     token to submit to <see cref="TwoFactorLogin" />.
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

        // SignInResult.RequiresTwoFactor is deliberately never set by CheckPasswordSignInAsync
        // itself -- found only by a real end-to-end smoke test, not by inspection -- that field
        // only ever gets populated by the higher-level PasswordSignInAsync/SignInOrTwoFactorAsync
        // flow, which this project cannot use here since it also writes an authentication
        // cookie via a scheme ("Identity.Application") this stateless, JWT-only API never
        // registers. Checking the same underlying condition SignInManager's own (protected,
        // inaccessible) IsTwoFactorEnabledAsync uses gets the same answer without that side
        // effect.
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
    ///     This action completes a sign-in <see cref="Login" /> flagged as requiring a second
    ///     factor, by checking the submitted code against the pending sign-in's own account.
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

        // A recovery code is redeemed (checked and, on success, invalidated in the same call),
        // not merely verified the way an authenticator/e-mailed code is -- a real second-factor
        // provider check would have nothing to check a recovery code's own value against, since
        // it is not a TOTP/e-mail token generated by either registered provider at all.
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
    ///     This action exchanges a still-valid refresh token for a new access/refresh token pair,
    ///     revoking the refresh token it was exchanged for (rotation) so it cannot be reused. The
    ///     token comes from the request body when one is named there, otherwise from the browser's
    ///     refresh cookie -- in which case the rotated token goes back as a new cookie, not in the
    ///     body. The new session keeps the old one's <see cref="SessionEntity.IsPersistent" />
    ///     choice.
    ///     <para>
    ///         A failed cookie exchange deliberately leaves the cookie alone rather than clearing
    ///         it. Two tabs reloading together both present the same cookie; the first rotates it,
    ///         and the second's now-stale request fails. Had that failure cleared the cookie, its
    ///         response would erase the fresh cookie the first tab just received and sign both
    ///         out. Left alone, the second tab simply retries and the browser sends the rotated
    ///         cookie. A genuinely dead cookie costs nothing but a 401; <see cref="Logout" />
    ///         clears it.
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
    ///     This action revokes a refresh token, so it can no longer be exchanged for a new access
    ///     token -- the one named in the body, otherwise the browser's refresh cookie, which is
    ///     cleared either way. Possessing the raw refresh token is itself sufficient authorization
    ///     to revoke it -- the same reasoning <see cref="Refresh" /> already applies -- so this
    ///     endpoint does not require a currently-valid access token.
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
    ///     This action completes a token-based password reset from this project's shared "set new
    ///     password" landing page -- the confirming half of both
    ///     <c>UsersController.ResetPassword</c>'s own <c>EmailLink</c> delivery mode and this
    ///     controller's own <see cref="ForgotPassword" /> above, which share this exact same
    ///     confirmation action unchanged. Neither caller is authenticated at this point, so this action is
    ///     <see cref="AllowAnonymousAttribute" />, exactly like <see cref="Login" />/
    ///     <see cref="Refresh" />/<see cref="Logout" /> above -- <see cref="ConfirmPasswordResetRequest.Token" />
    ///     is the only proof of authorization this action has, checked for real by
    ///     <see cref="UserManager{TUser}.ResetPasswordAsync" /> itself (a wrong/expired/already-
    ///     consumed token fails there, not here). A missing account or an unparseable token both
    ///     report the exact same generic message, deliberately, so this action never confirms or
    ///     denies whether a given account exists.
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
    ///     This method issues a fresh access/refresh token pair for a newly signed-in account
    ///     (<see cref="Login" />/<see cref="TwoFactorLogin" />). With cookie delivery the session is
    ///     persistent only when "Remember me" was checked; with body delivery it is always
    ///     persistent, exactly as every body-delivered session was before cookie delivery existed.
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
    ///     This method issues a fresh access/refresh token pair and wraps it in the shape every
    ///     completed sign-in action returns, delivering the refresh token as requested: in the
    ///     body, or only as <see cref="RefreshTokenCookie" />'s cookie with the body's own
    ///     refresh-token field left <see langword="null" />.
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
