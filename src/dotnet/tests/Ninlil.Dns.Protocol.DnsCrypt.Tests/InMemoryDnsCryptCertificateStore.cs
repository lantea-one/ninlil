using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class is an in-memory <see cref="IDnsCryptCertificateStore" /> fake. These tests cover
///     the listeners, not certificate storage (<c>Ninlil.Dns.Server.DnsCrypt.Tests</c> covers
///     that), so a file-backed store would only add unrelated I/O.
/// </summary>
internal sealed class InMemoryDnsCryptCertificateStore(DnssecKeyPair providerKeyPair)
    : IDnsCryptCertificateStore
{
    private List<DnsCryptCertificateEntry> _entries = [];

    /// <inheritdoc />
    public DnssecKeyPair ProviderKeyPair { get; } = providerKeyPair;

    /// <inheritdoc />
    public IReadOnlyList<DnsCryptCertificateEntry> Entries => _entries;

    /// <inheritdoc />
    public void Save(IReadOnlyList<DnsCryptCertificateEntry> entries) => _entries = [.. entries];
}
