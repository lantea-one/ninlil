// Define our namespace.
namespace Ninlil.Web.Options;

/// <summary>
///     This class binds to the <c>DataProtection</c> configuration section: the certificate that
///     encrypts the ASP.NET Core Data Protection key ring at rest. The key ring unlocks every
///     secret Ninlil stores (TSIG secrets, DNSSEC private keys, TOTP keys, the SSO client secret,
///     the SMTP password, cloud and webhook credentials, the ACME account key). <c>Program.cs</c>
///     reads the section before the key ring is configured, so a change needs a restart, and
///     <see cref="Ninlil.Web.DataProtection.KeyRingProtection" /> turns it into the protection the key ring uses.
///     <para>
///         With <see cref="CertificatePath" /> set, new keys are encrypted with that certificate
///         on every platform. With it empty, new keys are encrypted with Windows DPAPI on Windows
///         and are not encrypted elsewhere (the data directory is then owner-only). Keys written
///         earlier stay readable: a key file says how it was protected, and Data Protection reads
///         unencrypted, DPAPI and certificate keys alike.
///     </para>
/// </summary>
public sealed class DataProtectionKeyRingOptions
{
    /// <summary>
    ///     This constant contains the configuration section name this class binds to.
    /// </summary>
    public const string SectionName = "DataProtection";

    /// <summary>
    ///     This property contains the path of a PKCS#12 (<c>.pfx</c>) file with an RSA certificate
    ///     and its private key. New key ring entries are encrypted with the certificate. Empty
    ///     (the default) means Windows DPAPI on Windows and no encryption elsewhere. Configure a
    ///     certificate when a backup may be restored on another machine, because a DPAPI-protected
    ///     key ring can be read only on the machine and account that wrote it. Back the
    ///     certificate up separately from the data directory: without it the key ring, and so
    ///     every stored secret, cannot be read.
    /// </summary>
    public string CertificatePath { get; set; } = "";

    /// <summary>
    ///     This property contains the password of <see cref="CertificatePath" />. It is a secret:
    ///     set it from outside the application and never log it. Empty for a file without a
    ///     password.
    /// </summary>
    public string CertificatePassword { get; set; } = "";

    /// <summary>
    ///     This property contains the path of the certificate that was in use before
    ///     <see cref="CertificatePath" /> changed. While it is set, key ring entries written under
    ///     it still decrypt. Remove it once no entry uses it any more. Needs
    ///     <see cref="CertificatePath" />.
    /// </summary>
    public string PreviousCertificatePath { get; set; } = "";

    /// <summary>
    ///     This property contains the password of <see cref="PreviousCertificatePath" />. It is a
    ///     secret, like <see cref="CertificatePassword" />.
    /// </summary>
    public string PreviousCertificatePassword { get; set; } = "";
}
