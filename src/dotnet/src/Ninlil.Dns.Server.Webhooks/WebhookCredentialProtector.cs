using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks;

/// <summary>
///     This class encrypts and decrypts the two secret fields of a
///     <c>WebhookSubscriptionEntity</c>: the outbound auth credentials
///     (<c>AuthCredentialsEncrypted</c>) and the optional HMAC signing secret
///     (<c>HmacSigningSecretEncrypted</c>). It uses ASP.NET Core Data Protection
///     (<see cref="IDataProtector" />) rather than a custom scheme, like
///     <c>TsigSecretProtector</c> and <c>CloudCredentialProtector</c>. Each field has its own
///     purpose string, so one ciphertext cannot be decrypted as the other: the credentials
///     authenticate this server to the endpoint, the secret lets the endpoint verify payloads,
///     and the two are rotated independently. <c>WebhooksController</c> encrypts them
///     and <see cref="WebhookDeliveryService" /> decrypts them.
/// </summary>
/// <param name="dataProtectionProvider">The provider both purpose-scoped protectors are created from.</param>
public sealed class WebhookCredentialProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This field contains the protector for a subscription's outbound auth credentials, a
    ///     small JSON object whose shape depends on <c>WebhookAuthModeEnum</c>
    ///     (<c>{"BearerToken":"..."}</c> or <c>{"Username":"...","Password":"..."}</c>).
    /// </summary>
    private readonly IDataProtector _authCredentialsProtector =
        dataProtectionProvider.CreateProtector("Ninlil.Dns.Server.Webhooks.AuthCredentials");

    /// <summary>
    ///     This field contains the protector for a subscription's optional HMAC signing secret.
    /// </summary>
    private readonly IDataProtector _hmacSigningSecretProtector =
        dataProtectionProvider.CreateProtector("Ninlil.Dns.Server.Webhooks.HmacSigningSecret");

    /// <summary>
    ///     This method encrypts a subscription's auth credentials JSON for
    ///     <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c>.
    /// </summary>
    /// <param name="credentialsJson">The plaintext credentials JSON to encrypt.</param>
    public string ProtectAuthCredentials(string credentialsJson) =>
        _authCredentialsProtector.Protect(credentialsJson);

    /// <summary>
    ///     This method decrypts auth credentials JSON encrypted by
    ///     <see cref="ProtectAuthCredentials" />. It throws if the Data Protection key is no longer
    ///     available.
    /// </summary>
    /// <param name="authCredentialsEncrypted">
    ///     A <c>WebhookSubscriptionEntity.AuthCredentialsEncrypted</c> value.
    /// </param>
    public string UnprotectAuthCredentials(string authCredentialsEncrypted) =>
        _authCredentialsProtector.Unprotect(authCredentialsEncrypted);

    /// <summary>
    ///     This method encrypts a subscription's HMAC signing secret for
    ///     <c>WebhookSubscriptionEntity.HmacSigningSecretEncrypted</c>.
    /// </summary>
    /// <param name="secret">The plaintext HMAC signing secret to encrypt.</param>
    public string ProtectHmacSigningSecret(string secret) =>
        _hmacSigningSecretProtector.Protect(secret);

    /// <summary>
    ///     This method decrypts an HMAC signing secret encrypted by
    ///     <see cref="ProtectHmacSigningSecret" />. It throws if the Data Protection key is no
    ///     longer available.
    /// </summary>
    /// <param name="hmacSigningSecretEncrypted">
    ///     A <c>WebhookSubscriptionEntity.HmacSigningSecretEncrypted</c> value.
    /// </param>
    public string UnprotectHmacSigningSecret(string hmacSigningSecretEncrypted) =>
        _hmacSigningSecretProtector.Unprotect(hmacSigningSecretEncrypted);
}
