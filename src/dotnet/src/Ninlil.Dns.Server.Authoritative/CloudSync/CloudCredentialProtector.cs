using Microsoft.AspNetCore.DataProtection;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class encrypts and decrypts <see cref="Data.Abstractions.Entity.CloudDnsProviderCredentialEntity.CredentialsEncrypted" />:
///     a small JSON object of provider fields (an API token, or an access key ID and secret) held
///     with ASP.NET Core Data Protection, as <c>TsigSecretProtector</c> does for TSIG secrets.
/// </summary>
/// <param name="dataProtectionProvider">The provider the purpose-scoped protector is created from.</param>
public sealed class CloudCredentialProtector(IDataProtectionProvider dataProtectionProvider)
{
    /// <summary>
    ///     This field contains the protector. Its purpose string is used for nothing else, so Data
    ///     Protection derives a key not shared with TSIG secrets, DNSSEC keys or other protected
    ///     data.
    /// </summary>
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Ninlil.Dns.Server.Authoritative.CloudSync.Credentials"
    );

    /// <summary>
    ///     This method encrypts a credential set's fields into the string a
    ///     <c>CloudDnsProviderCredentialEntity</c> stores.
    /// </summary>
    public string Protect(IReadOnlyDictionary<string, string> credentials) =>
        _protector.Protect(Ninlil.Common.Serializer.JsonSerializer.Serialize(credentials));

    /// <summary>
    ///     This method decrypts a stored <c>CredentialsEncrypted</c> value into its fields.
    /// </summary>
    public IReadOnlyDictionary<string, string> Unprotect(string credentialsEncrypted) =>
        Ninlil.Common.Serializer.JsonSerializer.Deserialize<Dictionary<string, string>>(
            _protector.Unprotect(credentialsEncrypted)
        ) ?? [];
}
