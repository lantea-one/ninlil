// Define our namespace.
namespace Ninlil.Web.DataProtection;

/// <summary>
///     This enum names how new Data Protection key ring entries are encrypted at rest. It is the
///     outcome of <see cref="KeyRingProtection.Resolve" />, in the order of precedence the
///     settings give.
/// </summary>
public enum KeyRingProtectionMode
{
    /// <summary>
    ///     New entries are not encrypted. This applies when no certificate is configured on a
    ///     platform other than Windows; the owner-only data directory is the protection.
    /// </summary>
    None,

    /// <summary>
    ///     New entries are encrypted with the certificate in
    ///     <see cref="Options.DataProtectionKeyRingOptions.CertificatePath" />, on any platform.
    /// </summary>
    Certificate,

    /// <summary>
    ///     New entries are encrypted with Windows DPAPI for the account the process runs as. This
    ///     applies on Windows when no certificate is configured.
    /// </summary>
    Dpapi,
}
