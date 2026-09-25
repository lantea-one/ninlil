using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt;

/// <summary>
///     This interface stores the resolver's DNSCrypt identity across restarts: the long-term
///     Ed25519 provider key pair that signs every certificate, and the tracked certificates.
///     <see cref="DnsCryptCertificateRotationService" /> writes it and the DNSCrypt listeners
///     read it. <see cref="FileDnsCryptCertificateStore" /> implements it.
/// </summary>
public interface IDnsCryptCertificateStore
{
    /// <summary>
    ///     This property contains the long-term provider key pair, generated once and kept for
    ///     the lifetime of the installation.
    /// </summary>
    DnssecKeyPair ProviderKeyPair { get; }

    /// <summary>
    ///     This property contains every tracked certificate, valid or not.
    /// </summary>
    IReadOnlyList<DnsCryptCertificateEntry> Entries { get; }

    /// <summary>
    ///     This method replaces the whole tracked set, in memory and in durable storage.
    /// </summary>
    /// <param name="entries">The complete, new set of tracked certificates.</param>
    void Save(IReadOnlyList<DnsCryptCertificateEntry> entries);
}
