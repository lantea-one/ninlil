using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This interface holds this resolver's own DNSCrypt identity durably: its long-term
///     Ed25519 provider key pair (which signs every certificate) and every certificate
///     currently being tracked, across restarts.
/// </summary>
public interface IDnsCryptCertificateStore
{
    /// <summary>
    ///     This property contains this resolver's own long-term provider key pair, generated
    ///     once and reused for the lifetime of this installation.
    /// </summary>
    DnssecKeyPair ProviderKeyPair { get; }

    /// <summary>
    ///     This property contains every certificate currently being tracked, regardless of
    ///     whether it is still valid.
    /// </summary>
    IReadOnlyList<DnsCryptCertificateEntry> Entries { get; }

    /// <summary>
    ///     This method replaces this store's entire tracked set, both in memory and on whatever
    ///     durable storage backs it.
    /// </summary>
    /// <param name="entries">The complete, new set of tracked certificates.</param>
    void Save(IReadOnlyList<DnsCryptCertificateEntry> entries);
}
