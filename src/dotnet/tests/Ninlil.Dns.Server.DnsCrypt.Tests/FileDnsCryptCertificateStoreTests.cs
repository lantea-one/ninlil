using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.DnsCrypt;

// Define our namespace.
namespace Ninlil.Dns.Server.DnsCrypt.Tests;

/// <summary>
///     This class tests <see cref="FileDnsCryptCertificateStore" /> with a state file in the
///     temporary directory: a second instance reloads the provider key and saved certificates.
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
}
