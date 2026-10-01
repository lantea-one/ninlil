using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class builds and sends the "reset your password" e-mail for both e-mailed reset
///     flows: the <c>EmailLink</c> mode of an administrator's reset
///     (<c>UsersController.ResetPassword</c>) and the self-service Forgot Password request
///     (<c>AuthController.ForgotPassword</c>). It generates a token with
///     <see cref="UserManager{TUser}.GeneratePasswordResetTokenAsync" />, encodes it into a link
///     and sends it through <see cref="IEmailSender" />.
///     <para>
///         Each caller decides for itself <b>whether</b> to send, because the right answers
///         differ. An administrator's reset reports a missing e-mail address or an unconfigured
///         SMTP server as an error, since an authenticated administrator is acting on a known
///         account. Forgot Password never reveals either condition, so as not to leak whether an
///         address has an account; it simply does not call this method.
///     </para>
/// </summary>
/// <param name="userManager">Generates the Identity password reset token.</param>
/// <param name="emailSender">Delivers the resulting message.</param>
public class PasswordResetLinkSender(UserManager<UserEntity> userManager, IEmailSender emailSender)
{
    /// <summary>
    ///     This method generates a password reset token for <paramref name="user" /> and e-mails it
    ///     as a link to the admin console's <c>/reset-password</c> page, whose form submits to
    ///     <c>AuthController.ResetPassword</c>.
    /// </summary>
    /// <param name="user">The account to send a reset link for. Must carry a non-empty e-mail address.</param>
    /// <param name="requestScheme">
    ///     The current request's scheme (<c>Request.Scheme</c>). The REST API and the admin console
    ///     are served by one host, so the link is built from the request that asked for it; there
    ///     is no separate public URL setting. The request's Host header is not checked against an
    ///     allowlist here.
    /// </param>
    /// <param name="requestHost">The current request's host (<c>Request.Host</c>).</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task SendAsync(
        UserEntity user,
        string requestScheme,
        HostString requestHost,
        CancellationToken stoppingToken
    )
    {
        string rawToken = await userManager.GeneratePasswordResetTokenAsync(user);

        // A raw Identity token can contain '+', '/' and '=', which do not survive a query string
        // reliably, so it is Base64Url-encoded; AuthController.ResetPassword decodes it.
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
