using System.Net;
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
///     This class builds and sends the e-mails of the self-service e-mail change: a confirmation
///     link for the new address and a notice for the old one. It generates a token with
///     <see cref="UserManager{TUser}.GenerateChangeEmailTokenAsync" />, encodes it into a link
///     under <see cref="EmailSenderOptions.PublicBaseUrl" /> and sends through
///     <see cref="IEmailSender" />. The link targets the admin console route
///     <c>/confirm-email-change</c>, which submits the values back to
///     <see cref="Controllers.AuthController.ConfirmEmailChange" />. That action calls
///     <see cref="UserManager{TUser}.ChangeEmailAsync" /> to apply the change.
///     <see cref="Controllers.UsersController.ChangeMyEmail" /> sends the confirmation link, and
///     both actions send the notice to the old address.
///     <para>
///         The link never uses the request's Host header or scheme. A caller can forge Host, and a
///         link built from it would hand the token to the forger's server. Without a configured
///         base URL no link is built at all. The base URL is validated by
///         <see cref="PasswordResetLinkSender.TryParsePublicBaseUrl" />, so both senders accept the
///         same values.
///     </para>
///     <para>
///         The class only builds and sends. The caller decides whether a message is due, for
///         example whether SMTP is configured.
///     </para>
/// </summary>
/// <param name="userManager">Generates the Identity change-email token.</param>
/// <param name="emailSender">Delivers the resulting messages.</param>
/// <param name="emailOptions">Supplies <see cref="EmailSenderOptions.PublicBaseUrl" />.</param>
public class EmailChangeLinkSender(
    UserManager<UserEntity> userManager,
    IEmailSender emailSender,
    IOptionsMonitor<EmailSenderOptions> emailOptions
)
{
    /// <summary>
    ///     This constant contains the error a caller sees when no
    ///     <see cref="EmailSenderOptions.PublicBaseUrl" /> is configured.
    /// </summary>
    public const string PublicBaseUrlNotConfiguredMessage =
        "No public base URL is configured, so an e-mail confirmation link cannot be built. Set "
        + "the admin console's public address in the SMTP settings ('publicBaseUrl', or the "
        + "NINLIL_EMAIL_PUBLIC_BASE_URL environment variable) first.";

    /// <summary>
    ///     This method generates a change-email token for <paramref name="user" /> and
    ///     <paramref name="newEmail" /> and returns the confirmation link, without sending it.
    /// </summary>
    /// <param name="user">The account whose address is to change.</param>
    /// <param name="newEmail">The address the change is for. The token is bound to it.</param>
    /// <returns>The absolute link under <see cref="EmailSenderOptions.PublicBaseUrl" />.</returns>
    /// <exception cref="ValidationException">Thrown when no public base URL is configured.</exception>
    public async Task<string> CreateConfirmationLinkAsync(UserEntity user, string newEmail)
    {
        if (
            !PasswordResetLinkSender.TryParsePublicBaseUrl(
                emailOptions.CurrentValue.PublicBaseUrl,
                out Uri baseUri
            )
        )
            throw new ValidationException(PublicBaseUrlNotConfiguredMessage);

        string rawToken = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);

        // A raw Identity token can contain '+', '/' and '=', which do not survive a query string
        // reliably, so it is Base64Url-encoded. Every value is escaped because the new address is
        // user input and may hold '+', '&' or '#', which would otherwise change the query.
        string encodedToken = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(rawToken));

        return $"{baseUri.AbsoluteUri.TrimEnd('/')}/confirm-email-change"
            + $"?userId={Uri.EscapeDataString(user.Id.ToString())}"
            + $"&email={Uri.EscapeDataString(newEmail)}"
            + $"&token={Uri.EscapeDataString(encodedToken)}";
    }

    /// <summary>
    ///     This method e-mails the confirmation link to the new address.
    /// </summary>
    /// <param name="newEmail">The address to confirm.</param>
    /// <param name="confirmationUrl">The link from <see cref="CreateConfirmationLinkAsync" />.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public Task SendConfirmationAsync(
        string newEmail,
        string confirmationUrl,
        CancellationToken stoppingToken
    ) =>
        emailSender.SendAsync(
            newEmail,
            "Confirm your new Ninlil e-mail address",
            // HTML-encoded so the URL cannot break out of the attribute.
            "<p>A Ninlil account asked to use this e-mail address. "
                + $"<a href=\"{WebUtility.HtmlEncode(confirmationUrl)}\">Click here to confirm "
                + "your new e-mail address</a>. If you did not ask for this, ignore this "
                + "e-mail.</p>",
            stoppingToken
        );

    /// <summary>
    ///     This method tells the old address that the account's address was changed.
    /// </summary>
    /// <param name="oldEmail">The address the account had.</param>
    /// <param name="newEmail">The address the account has now.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public Task SendChangedNoticeAsync(
        string oldEmail,
        string newEmail,
        CancellationToken stoppingToken
    ) =>
        emailSender.SendAsync(
            oldEmail,
            "Your Ninlil e-mail address was changed",
            "<p>The e-mail address of your Ninlil account was changed to "
                + $"<b>{WebUtility.HtmlEncode(newEmail)}</b>. If you did not make this change, "
                + "contact an administrator.</p>",
            stoppingToken
        );
}
