using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class is a minimal, in-memory <see cref="IDnsCryptCertificateStore" /> test double --
///     this suite exercises the wire listener, not certificate persistence (that is
///     <c>Ninlil.Dns.Server.DnsCrypt.Tests</c>'s own job), so a real file-backed store would
///     only add unrelated I/O to every test.
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
