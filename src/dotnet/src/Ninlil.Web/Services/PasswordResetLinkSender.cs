using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Web.Services;

/// <summary>
///     This class builds and sends the "reset your password" e-mail for both e-mailed reset
///     flows: the <c>EmailLink</c> mode of an administrator's reset
///     (<c>UsersController.ResetPassword</c>) and the self-service Forgot Password request
///     (<c>AuthController.ForgotPassword</c>). It generates a token with
///     <see cref="UserManager{TUser}.GeneratePasswordResetTokenAsync" />, encodes it into a link
///     under <see cref="EmailSenderOptions.PublicBaseUrl" /> and sends it through
///     <see cref="IEmailSender" />.
///     <para>
///         The link never uses the request's Host header or scheme. A caller can forge Host, and
///         a link built from it would hand the victim's reset token to the forger's server
///         (password-reset poisoning). Without a configured base URL no link is built at all.
///     </para>
///     <para>
///         Each caller decides for itself <b>whether</b> to send, because the right answers
///         differ. An administrator's reset reports a missing e-mail address, an unconfigured
///         SMTP server or a missing base URL as an error, since an authenticated administrator is
///         acting on a known account. Forgot Password never reveals any of these, so as not to
///         leak whether an address has an account; it simply does not send.
///     </para>
/// </summary>
/// <param name="userManager">Generates the Identity password reset token.</param>
/// <param name="emailSender">Delivers the resulting message.</param>
/// <param name="emailOptions">Supplies <see cref="EmailSenderOptions.PublicBaseUrl" />.</param>
public class PasswordResetLinkSender(
    UserManager<UserEntity> userManager,
    IEmailSender emailSender,
    IOptionsMonitor<EmailSenderOptions> emailOptions
)
{
    /// <summary>
    ///     This constant contains the error an administrator sees when no
    ///     <see cref="EmailSenderOptions.PublicBaseUrl" /> is configured.
    /// </summary>
    public const string PublicBaseUrlNotConfiguredMessage =
        "No public base URL is configured, so a password reset link cannot be built. Set the "
        + "admin console's public address in the SMTP settings ('publicBaseUrl', or the "
        + "NINLIL_EMAIL_PUBLIC_BASE_URL environment variable) first.";

    /// <summary>
    ///     This property indicates whether <see cref="EmailSenderOptions.PublicBaseUrl" /> currently
    ///     holds a usable URL, so a reset link can be built.
    /// </summary>
    public bool IsPublicBaseUrlConfigured =>
        TryParsePublicBaseUrl(emailOptions.CurrentValue.PublicBaseUrl, out _);

    /// <summary>
    ///     This method checks a <see cref="EmailSenderOptions.PublicBaseUrl" /> value: an absolute
    ///     <c>http</c> or <c>https</c> URL with no query string or fragment. A path is allowed, for
    ///     a console served under a reverse-proxy prefix.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <param name="baseUri">The parsed URL when the value is usable.</param>
    /// <returns><see langword="true" /> when the value is usable.</returns>
    public static bool TryParsePublicBaseUrl(string value, out Uri baseUri)
    {
        baseUri = null;

        if (
            string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(parsed.Query)
            || !string.IsNullOrEmpty(parsed.Fragment)
        )
            return false;

        baseUri = parsed;
        return true;
    }

    /// <summary>
    ///     This method generates a password reset token for <paramref name="user" /> and e-mails it
    ///     as a link to the admin console's <c>/reset-password</c> page, whose form submits to
    ///     <c>AuthController.ResetPassword</c>.
    /// </summary>
    /// <param name="user">The account to send a reset link for. Must carry a non-empty e-mail address.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <exception cref="ValidationException">Thrown when no public base URL is configured.</exception>
    public async Task SendAsync(UserEntity user, CancellationToken stoppingToken)
    {
        string resetUrl = await CreateResetLinkAsync(user);

        await SendLinkAsync(emailSender, user.Email, resetUrl, stoppingToken);
    }

    /// <summary>
    ///     This method generates a password reset token for <paramref name="user" /> and returns
    ///     the link to e-mail, without sending it. <c>AuthController.ForgotPassword</c> uses it to
    ///     send in the background.
    /// </summary>
    /// <param name="user">The account to build a reset link for.</param>
    /// <returns>The absolute reset link under <see cref="EmailSenderOptions.PublicBaseUrl" />.</returns>
    /// <exception cref="ValidationException">Thrown when no public base URL is configured.</exception>
    public async Task<string> CreateResetLinkAsync(UserEntity user)
    {
        if (!TryParsePublicBaseUrl(emailOptions.CurrentValue.PublicBaseUrl, out Uri baseUri))
            throw new ValidationException(PublicBaseUrlNotConfiguredMessage);

        string rawToken = await userManager.GeneratePasswordResetTokenAsync(user);

        // A raw Identity token can contain '+', '/' and '=', which do not survive a query string
        // reliably, so it is Base64Url-encoded; AuthController.ResetPassword decodes it.
        string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        return $"{baseUri.AbsoluteUri.TrimEnd('/')}/reset-password"
            + $"?userId={user.Id}&token={Uri.EscapeDataString(encodedToken)}";
    }

    /// <summary>
    ///     This method e-mails an already built reset link. It is static so a caller can send
    ///     from its own service scope after the request that built the link has ended.
    /// </summary>
    /// <param name="emailSender">Delivers the message.</param>
    /// <param name="toAddress">The account's e-mail address.</param>
    /// <param name="resetUrl">The link from <see cref="CreateResetLinkAsync" />.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public static Task SendLinkAsync(
        IEmailSender emailSender,
        string toAddress,
        string resetUrl,
        CancellationToken stoppingToken
    ) =>
        emailSender.SendAsync(
            toAddress,
            "Reset your Ninlil password",
            "<p>A password reset was requested for your account. "
                + $"<a href=\"{resetUrl}\">Click here to set a new password</a>. If you did not "
                + "expect this, you can safely ignore this e-mail.</p>",
            stoppingToken
        );
}
