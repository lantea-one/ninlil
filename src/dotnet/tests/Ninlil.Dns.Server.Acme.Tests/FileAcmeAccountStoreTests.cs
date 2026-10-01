using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.Acme;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class tests <see cref="FileAcmeAccountStore" /> with a state file in a temporary
///     directory: what is saved is reloaded by a new instance, the file is encrypted, and an
///     unreadable file starts an empty state. Writer and reader share one
///     <see cref="IDataProtectionProvider" />, because each
///     <see cref="EphemeralDataProtectionProvider" /> has its own random key.
/// </summary>
public sealed class FileAcmeAccountStoreTests : IDisposable
{
    private readonly string _statePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-acme-tests-{Guid.NewGuid():N}.json"
    );

    public void Dispose()
    {
        if (File.Exists(_statePath))
            File.Delete(_statePath);
    }

    private FileAcmeAccountStore CreateStore(IDataProtectionProvider provider) =>
        new(
            provider,
            Options.Create(new AcmeOptions { AccountStateFilePath = _statePath }),
            NullLogger<FileAcmeAccountStore>.Instance
        );

    [Fact]
    public void AFreshStoreWithNoFileYetHasNoAccountKeyAndNoDomainStates()
    {
        FileAcmeAccountStore store = CreateStore(new EphemeralDataProtectionProvider());

        Assert.Null(store.AccountKeyPem);
        Assert.Empty(store.DomainStates);
    }

    [Fact]
    public void SaveAccountKeyThenReloadingFromANewInstanceRecoversTheSameKey()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CreateStore(provider)
            .SaveAccountKey("-----BEGIN PRIVATE KEY-----\nfake\n-----END PRIVATE KEY-----");

        FileAcmeAccountStore reloaded = CreateStore(provider);

        Assert.Equal(
            "-----BEGIN PRIVATE KEY-----\nfake\n-----END PRIVATE KEY-----",
            reloaded.AccountKeyPem
        );
    }

    [Fact]
    public void SaveDomainStateThenReloadingFromANewInstanceRecoversTheSameState()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        AcmeDomainCertificateState state = new(
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddDays(90),
            "/data/acme-certificates/example.com.pfx"
        );

        CreateStore(provider).SaveDomainState("example.com", state);

        FileAcmeAccountStore reloaded = CreateStore(provider);

        Assert.True(
            reloaded.DomainStates.TryGetValue(
                "example.com",
                out AcmeDomainCertificateState reloadedState
            )
        );
        Assert.Equal(state.PfxPath, reloadedState.PfxPath);
        Assert.Equal(state.ExpiresAtUtc, reloadedState.ExpiresAtUtc);
    }

    [Fact]
    public void TheOnDiskFileDoesNotContainThePlaintextAccountKey()
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CreateStore(provider).SaveAccountKey("super-secret-key-material");

        string onDisk = File.ReadAllText(_statePath);

        Assert.DoesNotContain("super-secret-key-material", onDisk);
    }

    [Fact]
    public void AnIndependentDataProtectionProviderTreatsAnUndecryptableFileAsFreshSeedRatherThanThrowing()
    {
        // Unlike CloudCredentialProtector, which lets a decryption failure propagate,
        // FileAcmeAccountStore.LoadOrSeed catches CryptographicException and starts from an
        // empty state, as the other file-backed stores (FileTrustAnchorStore,
        // FileDnsCryptCertificateStore) do for an unreadable file.
        CreateStore(new EphemeralDataProtectionProvider()).SaveAccountKey("some-key");

        FileAcmeAccountStore otherProviderStore = CreateStore(
            new EphemeralDataProtectionProvider()
        );

        Assert.Null(otherProviderStore.AccountKeyPem);
    }
}
