using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Certificates;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Certificates;

/// <summary>
///     This class tests how <see cref="TlsCertificateLoader" /> saves the self-signed
///     certificate it generates, using a state file in a temporary directory.
/// </summary>
public sealed class TlsCertificateLoaderPersistTests : IDisposable
{
    private readonly string _directory = Directory
        .CreateTempSubdirectory("ninlil-tls-tests-")
        .FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void TheGeneratedSelfSignedPfxIsSavedOwnerOnlyAndLoadsAgain()
    {
        string statePath = Path.Combine(_directory, "tls", "self-signed.pfx");
        TlsCertificateOptions options = new() { SelfSignedCertificateStatePath = statePath };

        using var generated = TlsCertificateLoader.LoadOrGenerate(options, NullLogger.Instance);

        Assert.True(File.Exists(statePath));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(statePath), "*.tmp"));

        if (!OperatingSystem.IsWindows())
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(statePath)
            );

        using var reloaded = TlsCertificateLoader.LoadOrGenerate(options, NullLogger.Instance);
        Assert.Equal(generated.Thumbprint, reloaded.Thumbprint);
    }
}
