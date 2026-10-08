using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="FileDnsCryptCertificateStore" /> with a state file in the
///     temporary directory: a second instance reloads the provider key and saved certificates, a
///     damaged file is moved aside and replaced by a new identity, and the file is owner-only.
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
            _ = new FileDnsCryptCertificateStore(
                Options.Create(new DnsCryptOptions { CertificateStateFilePath = statePath }),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

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
}
