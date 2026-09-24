using Microsoft.AspNetCore.DataProtection;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Sso;

/// <summary>
///     This class encrypts and decrypts <c>OidcSsoOptions.ClientSecretEncrypted</c> via
///     <see cref="IDataProtector" />, the same "encrypted at rest via ASP.NET Core Data
///     Protection, never a custom crypto scheme, one purpose-scoped protector per secret type"
///     pattern <c>Server.Webhooks.WebhookCredentialProtector</c>/<c>Server.Tsig.TsigSecretProtector</c>
///     already establish (architectural invariant #5).
///     <para>
///         <b>This is the first <c>Options</c>-class secret field this project encrypts at rest
///         before writing it into the runtime JSON configuration file.</b> Every prior runtime
///         settings secret field follows one of two shapes: it lives on a database-backed entity
///         (TSIG keys, DNSSEC keys, cloud credentials, webhook auth/HMAC secrets), where
///         invariant #5 already applies, or it is an <c>Options</c>-bound configuration-file
///         field written in plain text -- <c>EmailSenderOptions.Password</c>
///         (<c>Web.Controllers.SmtpSettingsController</c>) is the standing example, and stays
///         that way here: fixing that pre-existing gap is out of scope for this feature and is
///         deliberately left alone, not silently swept in. <see cref="OidcSsoOptions.ClientSecretEncrypted" />
///         does not repeat that gap -- it is the first configuration-file secret in this project
///         encrypted the same way a database-backed one already is, closing the gap invariant #5
///         calls for in general, for this one new field specifically.
///     </para>
/// </summary>
/// <param name="dataProtectionProvider">The provider this protector derives its own purpose-scoped key from.</param>
public sealed class OidcClientSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This property contains the dedicated, purpose-scoped protector used for this secret --
    ///     a compromise of a different secret's own protector must never also compromise this
    ///     one, since each is rotatable independently of the others.
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
    ///     This method decrypts a previously-encrypted <see cref="OidcSsoOptions.ClientSecretEncrypted" />
    ///     value, ready to hand to the OpenID Connect middleware as its own real
    ///     <c>OpenIdConnectOptions.ClientSecret</c>.
    /// </summary>
    /// <param name="clientSecretEncrypted">An <see cref="OidcSsoOptions.ClientSecretEncrypted" /> value.</param>
    public string Unprotect(string clientSecretEncrypted) =>
        string.IsNullOrEmpty(clientSecretEncrypted)
            ? string.Empty
            : _protector.Unprotect(clientSecretEncrypted);
}
