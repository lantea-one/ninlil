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
///     unreadable or damaged file is moved aside and starts an empty state, and the file is
///     owner-only. Writer and reader share one
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

        foreach (
            string aside in Directory.GetFiles(
                Path.GetTempPath(),
                $"{Path.GetFileName(_statePath)}.corrupt-*"
            )
        )
            File.Delete(aside);
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

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("null")]
    public void ADecryptableButUnusableFileIsMovedAsideAndStartsAFreshState(string plaintext)
    {
        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        File.WriteAllText(
            _statePath,
            provider.CreateProtector("Ninlil.Dns.Server.Acme.AccountState").Protect(plaintext)
        );

        FileAcmeAccountStore store = CreateStore(provider);

        Assert.Null(store.AccountKeyPem);
        Assert.Empty(store.DomainStates);
        Assert.Empty(store.Failures);
        Assert.Single(
            Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(_statePath)}.corrupt-*")
        );
        Assert.False(File.Exists(_statePath));
    }

    [Fact]
    public void ASavedStateFileIsOwnerOnlyAndLeavesNoTemporaryFile()
    {
        if (OperatingSystem.IsWindows())
            return;

        CreateStore(new EphemeralDataProtectionProvider()).SaveAccountKey("some-key");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(_statePath)
        );
        Assert.Empty(
            Directory.GetFiles(Path.GetTempPath(), $".{Path.GetFileName(_statePath)}.*.tmp")
        );
    }

    [Fact]
    public void AStateFileThatCannotBeOpenedThrowsAndStaysInPlaceUntilItCanBeRead()
    {
        // File mode 000 is a Unix permission; root reads the file anyway, so the case cannot run
        // as root.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;

        IDataProtectionProvider provider = new EphemeralDataProtectionProvider();
        CreateStore(provider).SaveAccountKey("the-account-key");
        File.SetUnixFileMode(_statePath, UnixFileMode.None);

        try
        {
            FileAcmeAccountStore store = CreateStore(provider);

            Assert.Throws<InvalidOperationException>(() => store.AccountKeyPem);

            // A transient error must not move the file aside or replace the account.
            Assert.True(File.Exists(_statePath));
            Assert.Empty(
                Directory.GetFiles(Path.GetTempPath(), $"{Path.GetFileName(_statePath)}.corrupt-*")
            );

            File.SetUnixFileMode(_statePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.Equal("the-account-key", store.AccountKeyPem);
        }
        finally
        {
            if (File.Exists(_statePath))
                File.SetUnixFileMode(_statePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
