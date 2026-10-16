using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="FileDnsCryptCertificateStore" /> with a state file in the
///     temporary directory: a second instance reloads the provider key and saved certificates, a
///     damaged file is moved aside and replaced by a new identity, the file is created on first use
///     and not at construction, an unreadable file is neither moved nor replaced, and the file is
///     owner-only.
/// </summary>
public class FileDnsCryptCertificateStoreTests
{
    [Fact]
    public void SecondInstance_ReloadsTheSameProviderKeyAndSavedEntries()
    {
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"dnscrypt-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new() { CertificateStateFilePath = statePath };
            IOptions<DnsCryptOptions> optionsWrapped = Options.Create(options);

            FileDnsCryptCertificateStore storeA = new(
                optionsWrapped,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            byte[] originalProviderPublicKey = storeA.ProviderKeyPair.PublicKeyWireFormat;

            DnsCryptCertificate certificate = new(
                DnsCryptEsVersion.XChaCha20Poly1305,
                new byte[32],
                new byte[8],
                1,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddHours(48),
                new byte[64]
            );
            storeA.Save([new DnsCryptCertificateEntry(certificate, new byte[32])]);

            FileDnsCryptCertificateStore storeB = new(
                optionsWrapped,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

            Assert.Equal(originalProviderPublicKey, storeB.ProviderKeyPair.PublicKeyWireFormat);
            Assert.Single(storeB.Entries);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{}")]
    [InlineData("{\"Certificates\":null}")]
    [InlineData("null")]
    public void ADamagedStateFile_IsMovedAsideAndReplacedByANewIdentity(string content)
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dnscrypt-test-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "state.json");
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(statePath, content);

            FileDnsCryptCertificateStore store = new(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

            Assert.NotEmpty(store.ProviderKeyPair.PublicKeyWireFormat);
            Assert.Empty(store.Entries);

            string aside = Assert.Single(Directory.GetFiles(directory, "state.json.corrupt-*"));
            Assert.Equal(content, File.ReadAllText(aside));

            // The replacement file is valid: a second instance loads the same identity.
            FileDnsCryptCertificateStore reloaded = new(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            Assert.Equal(
                store.ProviderKeyPair.PublicKeyWireFormat,
                reloaded.ProviderKeyPair.PublicKeyWireFormat
            );
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheStateFile_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
            return;

        string directory = Path.Combine(Path.GetTempPath(), $"dnscrypt-test-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "state.json");

        try
        {
            FileDnsCryptCertificateStore store = new(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            Assert.NotEmpty(store.ProviderKeyPair.PublicKeyWireFormat);

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(statePath)
            );
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Constructing_ForAMissingFile_CreatesNoFileUntilTheFirstRead()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dnscrypt-test-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "state.json");

        try
        {
            FileDnsCryptCertificateStore store = new(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

            Assert.False(File.Exists(statePath));
            Assert.False(Directory.Exists(directory));

            Assert.NotEmpty(store.ProviderKeyPair.PublicKeyWireFormat);
            Assert.True(File.Exists(statePath));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentFirstReads_GenerateOneIdentity()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"dnscrypt-test-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "state.json");

        try
        {
            FileDnsCryptCertificateStore store = new(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

            byte[][] keys = await Task.WhenAll(
                Enumerable
                    .Range(0, 16)
                    .Select(_ => Task.Run(() => store.ProviderKeyPair.PublicKeyWireFormat))
            );

            Assert.All(keys, key => Assert.Equal(keys[0], key));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AnUnreadableStateFile_Throws_KeepsTheFileAndLoadsTheOriginalKeyOnceReadable()
    {
        // Mode 000 does not stop root, and Windows has no Unix modes.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root")
            return;

        string directory = Path.Combine(Path.GetTempPath(), $"dnscrypt-test-{Guid.NewGuid():N}");
        string statePath = Path.Combine(directory, "state.json");
        IOptions<DnsCryptOptions> options = Options.Create(
            new DnsCryptOptions { CertificateStateFilePath = statePath }
        );

        try
        {
            byte[] original = new FileDnsCryptCertificateStore(
                options,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            )
                .ProviderKeyPair
                .PublicKeyWireFormat;
            byte[] contentBefore = File.ReadAllBytes(statePath);

            FileDnsCryptCertificateStore store = new(
                options,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            File.SetUnixFileMode(statePath, UnixFileMode.None);

            Assert.Throws<InvalidOperationException>(() => store.Entries);
            Assert.Throws<InvalidOperationException>(() => store.ProviderKeyPair);

            File.SetUnixFileMode(statePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Empty(Directory.GetFiles(directory, "*.corrupt-*"));
            Assert.Equal(contentBefore, File.ReadAllBytes(statePath));

            // The failure was not cached: the same instance now loads the original identity.
            Assert.Equal(original, store.ProviderKeyPair.PublicKeyWireFormat);
        }
        finally
        {
            if (File.Exists(statePath))
                File.SetUnixFileMode(statePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Directory.Delete(directory, recursive: true);
        }
    }
}
