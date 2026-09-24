using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class encrypts and decrypts a <see cref="Data.Abstractions.Entity.CloudDnsProviderCredentialEntity.CredentialsEncrypted" />
///     payload -- a small JSON object of provider-specific fields (an API token; an access key
///     ID and secret access key) -- via <see cref="IDataProtector" />, the same "encrypted at
///     rest via ASP.NET Core Data Protection, never a custom crypto scheme" pattern
///     <c>TsigService</c>'s own shared-secret handling already establishes.
/// </summary>
/// <param name="dataProtectionProvider">The provider this protector derives its own purpose-scoped key from.</param>
public sealed class CloudCredentialProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This property contains the actual protector, scoped to a purpose string unique to
    ///     this exact use -- Data Protection derives a distinct key per purpose string, so this
    ///     never shares a key with TSIG secrets, DNSSEC private keys, or anything else this
    ///     application separately protects.
    /// </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Authoritative.CloudSync.Credentials"
    );

    /// <summary>
    ///     This method encrypts a credential set's own fields into the string a
    ///     <c>CloudDnsProviderCredentialEntity</c> row actually stores.
    /// </summary>
    public string Protect(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(Ninlil.Common.Serializer.JsonSerializer.Serialize(credentials));

    /// <summary>
    ///     This method decrypts a stored <c>CredentialsEncrypted</c> value back into its
    ///     individual fields.
    /// </summary>
    public IReadOnlyDictionary<string, string> Unprotect(string credentialsEncrypted) =>
        Ninlil.Common.Serializer.JsonSerializer.Deserialize<Dictionary<string, string>>(
            _protector.Unprotect(credentialsEncrypted)
        ) ?? [];
}
