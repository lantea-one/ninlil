using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Tsig;

/// <summary>
///     This class encrypts and decrypts a TSIG key's shared secret at rest with ASP.NET Core Data
///     Protection. It keeps the purpose string <c>"Ninlil.Dns.Server.Tsig.SharedSecret"</c> in
///     one place, like <c>Ninlil.Dns.Server.Authoritative.CloudSync.CloudCredentialProtector</c>
///     does for cloud credentials. <see cref="TsigService" /> decrypts secrets to sign and verify;
///     the TSIG key endpoints in <c>Ninlil.Web</c> encrypt a new key's secret before it is stored.
///     The secret must be recoverable, not hashed, because computing a MAC needs the raw key.
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
