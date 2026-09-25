// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This enumeration lists the DNSCrypt "encryption system" (cipher suite) values a
///     certificate's <c>&lt;es-version&gt;</c> field selects.
/// </summary>
public enum DnsCryptEsVersion : ushort
{
    /// <summary>
    ///     X25519-XSalsa20Poly1305, the original DNSCrypt v2 cipher. It is dropped from the
    ///     current normative spec text but still supported by many clients.
    /// </summary>
    XSalsa20Poly1305 = 0x0001,

    /// <summary>
    ///     X25519-XChaCha20Poly1305 ("Box-XChaChaPoly"), the cipher the current DNSCrypt spec
    ///     requires.
    /// </summary>
    XChaCha20Poly1305 = 0x0002,
}
