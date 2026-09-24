using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class encrypts and decrypts a TSIG key's shared secret at rest, via ASP.NET Core Data
///     Protection -- the same mechanism <c>Ninlil.Dns.Identity</c> already uses for a user's
///     TOTP authenticator key. Extracted into its own class, mirroring
///     <c>Ninlil.Dns.Server.Authoritative.CloudSync.CloudCredentialProtector</c>'s own
///     precedent, so the purpose string <c>"Ninlil.Dns.Server.Tsig.SharedSecret"</c> has
///     exactly one place it is ever written, rather than being repeated as a literal everywhere a
///     TSIG secret is protected or unprotected: <see cref="TsigService" /> uses this for verifying
///     and signing, and <c>Ninlil.Web</c>'s own TSIG key management endpoints use it to
///     encrypt a newly created key's secret before it is ever persisted.
/// </summary>
/// <param name="dataProtectionProvider">The provider this protector is built from.</param>
public class TsigSecretProtector(IDataProtectionProvider dataProtectionProvider)
{
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Tsig.SharedSecret"
    );

    /// <summary>
    ///     This method encrypts a raw shared secret, ready to store in
    ///     <see cref="Data.Abstractions.Entity.TsigKeyEntity.SecretEncrypted" />.
    /// </summary>
    /// <param name="secret">The raw shared secret to encrypt.</param>
    public string Protect(byte[] secret) => _protector.Protect(Convert.ToBase64String(secret));

    /// <summary>
    ///     This method decrypts a shared secret previously encrypted by <see cref="Protect" />.
    /// </summary>
    /// <param name="secretEncrypted">
    ///     A <see cref="Data.Abstractions.Entity.TsigKeyEntity.SecretEncrypted" /> value.
    /// </param>
    public byte[] Unprotect(string secretEncrypted) =>
        Convert.FromBase64String(_protector.Unprotect(secretEncrypted));
}
