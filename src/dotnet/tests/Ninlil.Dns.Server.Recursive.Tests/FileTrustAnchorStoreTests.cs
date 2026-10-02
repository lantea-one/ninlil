using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Recursive.Validation;

namespace Ninlil.Dns.Server.Recursive.Tests;

/// <summary>
///     This class tests <see cref="FileTrustAnchorStore" /> with a state file in a temporary
///     directory: a corrupt file is replaced by the seed rather than stopping the store from
///     being created.
/// </summary>
public sealed class FileTrustAnchorStoreTests : IDisposable
{
    private readonly string _statePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-trust-anchor-tests-{Guid.NewGuid():N}.json"
    );

    public void Dispose()
    {
        if (File.Exists(_statePath))
            File.Delete(_statePath);
    }

    [Fact]
    public void ACorruptStateFileIsReseededFromTheRootTrustAnchors()
    {
        File.WriteAllText(_statePath, "{ this is not json");

        FileTrustAnchorStore store = new(
            Options.Create(new RecursiveServerOptions { TrustAnchorStateFilePath = _statePath }),
            NullLogger<FileTrustAnchorStore>.Instance
        );

        Assert.NotEmpty(store.GetValidAnchors());

        // The seed is written back, so the next start loads it instead of the corrupt file.
        FileTrustAnchorStore reloaded = new(
            Options.Create(new RecursiveServerOptions { TrustAnchorStateFilePath = _statePath }),
            NullLogger<FileTrustAnchorStore>.Instance
        );
        Assert.Equal(
            store.GetAll().Select(anchor => anchor.KeyTag),
            reloaded.GetAll().Select(anchor => anchor.KeyTag)
        );
    }
}
