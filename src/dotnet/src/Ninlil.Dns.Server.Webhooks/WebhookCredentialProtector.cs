using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class encrypts and decrypts a <c>WebhookSubscriptionEntity</c>'s own two secret
///     fields -- its outbound auth credential(s) (<c>AuthCredentialsEncrypted</c>) and its
///     optional HMAC payload-signing secret (<c>HmacSigningSecretEncrypted</c>) -- via
///     <see cref="IDataProtector" />, the same "encrypted at rest via ASP.NET Core Data
///     Protection, never a custom crypto scheme" pattern <c>TsigSecretProtector</c>/
///     <c>CloudCredentialProtector</c> already establish (architectural invariant #5). Each of
///     the two fields gets its own purpose-scoped protector -- a compromise of one must not also
///     compromise the other, since they protect genuinely independent secrets (one authenticates
///     this server *to* the subscriber's endpoint, the other lets the subscriber verify a payload
///     really came from this server) that an operator could rotate independently of each other.
/// </summary>
/// <param name="dataProtectionProvider">The provider both protectors derive their own purpose-scoped keys from.</param>
public sealed class WebhookCredentialProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This property contains the protector used for a subscription's own outbound auth
    ///     credential(s) -- a small JSON object shaped by <c>WebhookAuthModeEnum</c>
    ///     (<c>{"BearerToken":"..."}</c> or <c>{"Username":"...","Password":"..."}</c>).
    /// </summary>
    private readonly IDataProtector _authCredentialsProtector =
        dataProtectionProvider.CreateProtector("Ninlil.Dns.Server.Webhooks.AuthCredentials");

    /// <summary>
    ///     This property contains the protector used for a subscription's own optional HMAC
    ///     payload-signing secret.
    /// </summary>
    private readonly IDataProtector _hmacSigningSecretProtector =
        dataProtectionProvider.CreateProtector("Ninlil.Dns.Server.Webhooks.HmacSigningSecret");

    /// <summary>
    ///     This method encrypts a subscription's own outbound auth credential(s) JSON payload,
    ///     ready to store in <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c>.
    /// </summary>
    /// <param name="credentialsJson">The plaintext credential(s) JSON payload to encrypt.</param>
    public string ProtectAuthCredentials(string credentialsJson) =>
        _authCredentialsProtector.Protect(credentialsJson);

    /// <summary>
    ///     This method decrypts a subscription's own outbound auth credential(s) JSON payload,
    ///     previously encrypted by <see cref="ProtectAuthCredentials" />.
    /// </summary>
    /// <param name="authCredentialsEncrypted">
    ///     A <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c> value.
    /// </param>
    public string UnprotectAuthCredentials(string authCredentialsEncrypted) =>
        _authCredentialsProtector.Unprotect(authCredentialsEncrypted);

    /// <summary>
    ///     This method encrypts a subscription's own HMAC payload-signing secret, ready to store
    ///     in <c>WebhookSubscriptionEntity.HmacSigningSecretEncrypted</c>.
    /// </summary>
    /// <param name="secret">The plaintext HMAC signing secret to encrypt.</param>
    public string ProtectHmacSigningSecret(string secret) =>
        _hmacSigningSecretProtector.Protect(secret);

    /// <summary>
    ///     This method decrypts a subscription's own HMAC payload-signing secret, previously
    ///     encrypted by <see cref="ProtectHmacSigningSecret" />.
    /// </summary>
    /// <param name="hmacSigningSecretEncrypted">
    ///     A <c>WebhookSubscriptionEntity.HmacSigningSecretEncrypted</c> value.
    /// </param>
    public string UnprotectHmacSigningSecret(string hmacSigningSecretEncrypted) =>
        _hmacSigningSecretProtector.Unprotect(hmacSigningSecretEncrypted);
}
