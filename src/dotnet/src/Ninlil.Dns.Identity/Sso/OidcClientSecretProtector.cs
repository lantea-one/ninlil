using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This class encrypts and decrypts <c>OidcSsoOptions.ClientSecretEncrypted</c> with
///     <see cref="IDataProtector" />, using one purpose-scoped protector per secret type as
///     <c>Server.Webhooks.WebhookCredentialProtector</c> and <c>Server.Tsig.TsigSecretProtector</c>
///     do. Secrets are encrypted with ASP.NET Core Data Protection, never custom cryptography.
///     <para>
///         The client secret lives in the runtime JSON configuration file rather than the
///         database, and is encrypted there like the database-backed secrets (TSIG, DNSSEC and
///         cloud credentials, webhook secrets). The SMTP password is encrypted the same way, by
///         <c>Email.SmtpPasswordProtector</c>.
///     </para>
/// </summary>
/// <param name="dataProtectionProvider">The provider this protector derives its purpose-scoped key from.</param>
public sealed class OidcClientSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This field contains the protector for this secret. A separate purpose keeps it independent
    ///     of the protectors for other secrets.
    /// </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Identity.Sso.ClientSecret"
    );

    /// <summary>
    ///     This method encrypts a plaintext OAuth 2.0 client secret, ready to store in
    ///     <see cref="OidcSsoOptions.ClientSecretEncrypted" />.
    /// </summary>
    /// <param name="clientSecret">The plaintext client secret to encrypt.</param>
    public string Protect(string clientSecret) => _protector.Protect(clientSecret);

    /// <summary>
    ///     This method decrypts an <see cref="OidcSsoOptions.ClientSecretEncrypted" /> value for use as
    ///     <c>OpenIdConnectOptions.ClientSecret</c>. An empty value decrypts to an empty string.
    /// </summary>
    /// <param name="clientSecretEncrypted">An <see cref="OidcSsoOptions.ClientSecretEncrypted" /> value.</param>
    public string Unprotect(string clientSecretEncrypted) =>
        string.IsNullOrEmpty(clientSecretEncrypted)
            ? string.Empty
            : _protector.Unprotect(clientSecretEncrypted);

    /// <summary>
    ///     This method decrypts an <see cref="OidcSsoOptions.ClientSecretEncrypted" /> value without
    ///     throwing. A value that this server's key ring cannot read (a lost or replaced key ring,
    ///     or plaintext put into the encrypted setting) gives <see langword="false" />.
    /// </summary>
    /// <param name="clientSecretEncrypted">An <see cref="OidcSsoOptions.ClientSecretEncrypted" /> value.</param>
    /// <param name="plaintext">The decrypted secret, or an empty string on failure.</param>
    /// <returns><see langword="true" /> when the value is empty or was decrypted.</returns>
    public bool TryUnprotect(string clientSecretEncrypted, out string plaintext)
    {
        try
        {
            plaintext = Unprotect(clientSecretEncrypted);
            return true;
        }
        catch (CryptographicException)
        {
            plaintext = string.Empty;
            return false;
        }
    }

    /// <summary>
    ///     This method returns the client secret to use. A non-empty
    ///     <see cref="OidcSsoOptions.ClientSecret" /> wins, so an environment variable overrides the
    ///     configuration file, as <c>Email.SmtpPasswordProtector.ResolvePassword</c> does for SMTP.
    ///     Otherwise it decrypts <see cref="OidcSsoOptions.ClientSecretEncrypted" />.
    /// </summary>
    /// <param name="options">The SSO settings.</param>
    /// <param name="decrypted">
    ///     <see langword="false" /> when only an unreadable stored value was available; the result
    ///     is then empty.
    /// </param>
    /// <returns>The plaintext secret, or an empty string when none is set or readable.</returns>
    public string ResolveClientSecret(OidcSsoOptions options, out bool decrypted)
    {
        decrypted = true;

        if (!string.IsNullOrEmpty(options.ClientSecret))
            return options.ClientSecret;

        decrypted = TryUnprotect(options.ClientSecretEncrypted, out string plaintext);
        return plaintext;
    }

    /// <summary>
    ///     This method returns the client secret to use, or an empty string when none is set or
    ///     readable. See <see cref="ResolveClientSecret(OidcSsoOptions, out bool)" />.
    /// </summary>
    /// <param name="options">The SSO settings.</param>
    public string ResolveClientSecret(OidcSsoOptions options) =>
        ResolveClientSecret(options, out _);
}
