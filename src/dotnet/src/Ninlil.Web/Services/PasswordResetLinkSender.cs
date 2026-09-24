using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class builds and sends the "reset your password" e-mail every emailed password-reset
///     flow in this project shares. Originally written inline inside
///     <c>UsersController.ResetPassword</c>'s own <c>EmailLink</c> delivery mode; extracted here so
///     the self-service Forgot Password flow (<c>AuthController.ForgotPassword</c>) can send the
///     exact same message through the exact same real
///     <see cref="UserManager{TUser}.GeneratePasswordResetTokenAsync" />/Base64Url-encode/link-build/
///     <see cref="IEmailSender" /> round trip, rather than duplicating it a second time.
///     <para>
///         Both callers still make their own decision about <b>whether</b> to call this method at
///         all -- that decision is deliberately not shared, since the two callers' own correct
///         answers differ: an administrator-driven reset (<c>UsersController.ResetPassword</c>)
///         legitimately reports a missing e-mail address or an unconfigured SMTP server back to the
///         caller as a real, clear error, since an authenticated administrator is acting on a known
///         account. A self-service Forgot Password request (<c>AuthController.ForgotPassword</c>)
///         never reports either condition observably, to avoid leaking whether a given e-mail
///         address has an account at all -- it simply never calls this method when either condition
///         holds. This class only knows how to send the link once a caller has already decided to;
///         it does not itself decide when that's appropriate.
///     </para>
/// </summary>
/// <param name="userManager">Generates the real Identity password-reset token.</param>
/// <param name="emailSender">Delivers the resulting message.</param>
public class PasswordResetLinkSender(UserManager<UserEntity> userManager, IEmailSender emailSender)
{
    /// <summary>
    ///     This method generates a fresh password-reset token for <paramref name="user" /> and
    ///     e-mails it as a link to this project's shared "set new password" landing page
    ///     (<c>AuthController.ResetPassword</c> is what that page's own form submits to).
    /// </summary>
    /// <param name="user">The account to send a reset link for. Must carry a non-empty e-mail address.</param>
    /// <param name="requestScheme">
    ///     The current request's own scheme (<c>Request.Scheme</c>) -- this project's REST API and
    ///     Angular SPA share one combined host/process, so the link is always built from the
    ///     request that asked for it rather than a separate "public URL" setting.
    /// </param>
    /// <param name="requestHost">The current request's own host (<c>Request.Host</c>).</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task SendAsync(
        UserEntity user,
        string requestScheme,
        HostString requestHost,
        CancellationToken stoppingToken
    )
    {
        string rawToken = await userManager.GeneratePasswordResetTokenAsync(user);

        // A raw Identity reset token can contain '+'/'/'/'=', unsafe to place directly in a URL
        // query string -- Base64Url-encode it first (safe alphabet, no further escaping needed)
        // rather than relying on plain query-string escaping alone, which only protects the query
        // string's own syntax, not the token bytes themselves.
        string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        string resetUrl =
            $"{requestScheme}://{requestHost}/reset-password"
            + $"?userId={user.Id}&token={Uri.EscapeDataString(encodedToken)}";

        await emailSender.SendAsync(
            user.Email,
            "Reset your Ninlil password",
            "<p>A password reset was requested for your account. "
                + $"<a href=\"{resetUrl}\">Click here to set a new password</a>. If you did not "
                + "expect this, you can safely ignore this e-mail.</p>",
            stoppingToken
        );
    }
}
