// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This enumeration lists the DNSCrypt "encryption system" (cipher suite) values a
///     certificate's own <c>&lt;es-version&gt;</c> field selects between.
/// </summary>
public enum DnsCryptEsVersion : ushort
{
    /// <summary>
    ///     X25519-XSalsa20Poly1305 -- the original DNSCrypt v2 cipher. Dropped from the current
    ///     normative spec text, but still supported by many real-world clients.
    /// </summary>
    XSalsa20Poly1305 = 0x0001,

    /// <summary>
    ///     X25519-XChaCha20Poly1305 ("Box-XChaChaPoly") -- the cipher the current DNSCrypt spec
    ///     actually requires.
    /// </summary>
    XChaCha20Poly1305 = 0x0002,
}
