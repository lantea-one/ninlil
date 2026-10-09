using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Email;

/// <summary>
///     This class encrypts and decrypts <see cref="EmailSenderOptions.PasswordEncrypted" /> with
///     <see cref="IDataProtector" />, in the same way as <c>Sso.OidcClientSecretProtector</c>:
///     one purpose-scoped protector per secret type and no custom cryptography.
///     <para>
///         <c>Web.Controllers.SmtpSettingsController</c> encrypts the password before it reaches
///         the runtime configuration file, and <see cref="SmtpEmailSender" /> decrypts it when it
///         sends a message. <c>Web.Startup.SmtpPasswordMigrationService</c> encrypts a plaintext
///         value left in the file by an earlier version.
///     </para>
/// </summary>
/// <param name="dataProtectionProvider">The provider this protector derives its purpose-scoped key from.</param>
public sealed class SmtpPasswordProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This field contains the protector for this secret. A separate purpose keeps it
    ///     independent of the protectors for other secrets.
    /// </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Identity.Email.SmtpPassword"
    );

    /// <summary>
    ///     This method encrypts a plaintext SMTP password, ready to store in
    ///     <see cref="EmailSenderOptions.PasswordEncrypted" />.
    /// </summary>
    /// <param name="password">The plaintext password to encrypt.</param>
    public string Protect(string password) => _protector.Protect(password);

    /// <summary>
    ///     This method decrypts an <see cref="EmailSenderOptions.PasswordEncrypted" /> value. An
    ///     empty value decrypts to an empty string.
    /// </summary>
    /// <param name="passwordEncrypted">An <see cref="EmailSenderOptions.PasswordEncrypted" /> value.</param>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    ///     The value was not protected with this key ring.
    /// </exception>
    public string Unprotect(string passwordEncrypted) =>
        string.IsNullOrEmpty(passwordEncrypted)
            ? string.Empty
            : _protector.Unprotect(passwordEncrypted);

    /// <summary>
    ///     This method decrypts an <see cref="EmailSenderOptions.PasswordEncrypted" /> value without
    ///     throwing, for callers that only need to know whether the stored password is readable.
    /// </summary>
    /// <param name="passwordEncrypted">An <see cref="EmailSenderOptions.PasswordEncrypted" /> value.</param>
    /// <param name="plaintext">The decrypted password, or an empty string on failure.</param>
    /// <returns><see langword="true" /> when the value is empty or was decrypted.</returns>
    public bool TryUnprotect(string passwordEncrypted, out string plaintext)
    {
        try
        {
            plaintext = Unprotect(passwordEncrypted);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    /// <summary>
    ///     This method returns the password to authenticate with. A non-empty
    ///     <see cref="EmailSenderOptions.Password" /> wins, so an environment variable keeps
    ///     overriding the configuration file. Otherwise it decrypts
    ///     <see cref="EmailSenderOptions.PasswordEncrypted" />.
    /// </summary>
    /// <param name="options">The current SMTP settings.</param>
    /// <returns>The plaintext password, or <see langword="null" /> if none is set.</returns>
    public string ResolvePassword(EmailSenderOptions options)
    {
        if (!string.IsNullOrEmpty(options.Password))
            return options.Password;

        if (!string.IsNullOrEmpty(options.PasswordEncrypted))
            return Unprotect(options.PasswordEncrypted);

        return null;
    }
}
