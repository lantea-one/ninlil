using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class warns at startup about settings combinations that are accepted but do not do
///     what an operator probably expects. A hand-edited configuration file, a preset or an
///     environment variable can reach these states; the admin console cannot, so nothing else
///     reports them.
///     <para>
///         <c>Program.cs</c> calls <see cref="LogWarnings" /> once, next to
///         <see cref="PrivilegedPortStartupDiagnostics" />, after the options are bound. <b>It
///         only diagnoses:</b> a finding is logged and startup continues. Most findings are
///         Warnings; an SSO client secret that cannot be decrypted is an Error, because SSO
///         sign-in cannot work until someone acts. Each
///         check is a separate public method that returns the message or
///         <see langword="null" />, so a test can call it without a logger.
///     </para>
/// </summary>
public static class ConfigurationStartupDiagnostics
{
    /// <summary>
    ///     This method runs every check and logs one entry for each finding: a Warning, or an Error
    ///     for the SSO client secret.
    /// </summary>
    /// <param name="recursiveOptions">The bound <c>DnsServer:Recursive</c> settings.</param>
    /// <param name="emailOptions">The bound <c>Email</c> settings.</param>
    /// <param name="ssoOptions">The bound <c>Identity:Sso</c> settings.</param>
    /// <param name="dataProtectionProvider">Decrypts the stored SSO client secret to test it.</param>
    /// <param name="logger">Receives the findings.</param>
    public static void LogWarnings(
        RecursiveServerOptions recursiveOptions,
        EmailSenderOptions emailOptions,
        OidcSsoOptions ssoOptions,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger
    )
    {
        string[] warnings =
        [
            DescribeIgnoredInsecureDomains(recursiveOptions),
            DescribeSmtpUsernameWithoutPassword(emailOptions),
        ];

        foreach (string message in warnings)
        {
            if (message != null)
                logger.LogWarning("{Message}", message);
        }

        string ssoMessage = DescribeUnreadableSsoClientSecret(
            ssoOptions,
            new OidcClientSecretProtector(dataProtectionProvider)
        );

        if (ssoMessage != null)
            logger.LogError("{Message}", ssoMessage);
    }

    /// <summary>
    ///     This method reports an SSO client secret that cannot be decrypted. It applies only when
    ///     SSO is configured the way <c>ServiceCollectionAuthenticationExtension</c> registers it
    ///     (enabled, with an authority and a client id) and no plaintext
    ///     <c>Identity:Sso:ClientSecret</c> overrides the stored one. The server keeps running;
    ///     SSO sign-in fails at the provider. The message never contains the secret.
    /// </summary>
    /// <param name="options">The bound SSO settings.</param>
    /// <param name="protector">Tests whether the stored secret is readable.</param>
    /// <returns>The error text, or <see langword="null" /> when the settings agree.</returns>
    public static string DescribeUnreadableSsoClientSecret(
        OidcSsoOptions options,
        OidcClientSecretProtector protector
    )
    {
        bool configured =
            options.Enable
            && !string.IsNullOrWhiteSpace(options.Authority)
            && !string.IsNullOrWhiteSpace(options.ClientId);

        if (!configured || !string.IsNullOrEmpty(options.ClientSecret))
            return null;

        if (protector.TryUnprotect(options.ClientSecretEncrypted, out _))
            return null;

        return "Identity:Sso:ClientSecretEncrypted cannot be decrypted with this server's Data "
            + "Protection key ring, so SSO sign-in will fail at the identity provider. The rest "
            + "of the server is unaffected. Enter the client secret again on the SSO settings "
            + "page, or set NINLIL_IDENTITY_SSO_CLIENT_SECRET.";
    }

    /// <summary>
    ///     This method reports <c>DnsServer:Recursive:InsecureDomains</c> entries that have no
    ///     effect because <c>UseCustomInsecureDomains</c> is off: the resolver ignores the list
    ///     and uses the built-in one (see <see cref="RecursiveServerOptions.GetEffectiveInsecureDomains" />).
    /// </summary>
    /// <param name="options">The bound recursive resolver settings.</param>
    /// <returns>The warning text, or <see langword="null" /> when the settings agree.</returns>
    public static string DescribeIgnoredInsecureDomains(RecursiveServerOptions options)
    {
        if (options.UseCustomInsecureDomains)
            return null;

        if (options.InsecureDomains == null || !options.InsecureDomains.Any(IsNotBlank))
            return null;

        return "DnsServer:Recursive:InsecureDomains has entries, but "
            + "DnsServer:Recursive:UseCustomInsecureDomains is false, so the list is ignored and "
            + "the built-in Insecure domains are in force. Set UseCustomInsecureDomains "
            + "(NINLIL_DNS_SERVER_RECURSIVE_USE_CUSTOM_INSECURE_DOMAINS=true) to use the list.";
    }

    /// <summary>
    ///     This method reports an SMTP user name with no password from any source (neither
    ///     <c>Password</c> nor <c>PasswordEncrypted</c>). Authentication with a missing password
    ///     throws, so every e-mail send would fail.
    /// </summary>
    /// <param name="options">The bound SMTP settings.</param>
    /// <returns>The warning text, or <see langword="null" /> when the settings agree.</returns>
    public static string DescribeSmtpUsernameWithoutPassword(EmailSenderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Username))
            return null;

        if (
            !string.IsNullOrEmpty(options.Password)
            || !string.IsNullOrEmpty(options.PasswordEncrypted)
        )
            return null;

        return "Email:Username is set, but no password is configured (neither Email:Password nor "
            + "Email:PasswordEncrypted), so every e-mail send fails. Enter the password in the "
            + "SMTP settings or set NINLIL_EMAIL_PASSWORD, or clear the user name for a server "
            + "that accepts anonymous connections.";
    }

    private static bool IsNotBlank(string value) => !string.IsNullOrWhiteSpace(value);
}
