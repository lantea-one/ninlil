// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This enumeration lists the DNSCrypt "encryption system" (cipher suite) values a
///     certificate's <c>&lt;es-version&gt;</c> field selects.
/// </summary>
public enum DnsCryptEsVersion : ushort
{
    /// <summary>
    ///     X25519-XSalsa20Poly1305, the earlier DNSCrypt v2 construction. The current protocol
    ///     text (section 11) defines only X25519-XChaCha20Poly1305.
    /// </summary>
    XSalsa20Poly1305 = 0x0001,

    /// <summary>
    ///     X25519-XChaCha20Poly1305 ("Box-XChaChaPoly"), the cipher the current DNSCrypt v2
    ///     specification defines (section 11); a certificate names it with es-version
    ///     <c>0x00 0x02</c> (section 12).
    /// </summary>
    XChaCha20Poly1305 = 0x0002,
}
