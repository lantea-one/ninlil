// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests.Fakes;

/// <summary>
///     This class wraps a real <see cref="IDnsCryptCertificateStore" /> and throws from
///     <see cref="Entries" /> for the first <paramref name="failuresBeforeSuccess" /> reads, so a
///     test can check that <see cref="DnsCryptCertificateRotationService" /> survives a failed
///     startup check and retries.
/// </summary>
/// <param name="inner">The store every call is forwarded to once the failures are used up.</param>
/// <param name="failuresBeforeSuccess">How many reads of <see cref="Entries" /> throw.</param>
public sealed class FailingFirstReadCertificateStore(
    IDnsCryptCertificateStore inner,
    int failuresBeforeSuccess
) : IDnsCryptCertificateStore
{
    private int _remainingFailures = failuresBeforeSuccess;

    /// <inheritdoc />
    public Ninlil.Dns.Server.Dnssec.Algorithms.DnssecKeyPair ProviderKeyPair =>
        inner.ProviderKeyPair;

    /// <inheritdoc />
    public IReadOnlyList<DnsCryptCertificateEntry> Entries =>
        Interlocked.Decrement(ref _remainingFailures) >= 0
            ? throw new IOException("Simulated store failure.")
            : inner.Entries;

    /// <inheritdoc />
    public void Save(IReadOnlyList<DnsCryptCertificateEntry> entries) => inner.Save(entries);
}
