using System.Net;
using System.Net.Security;
using System.Net.Sockets;
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

    /// <summary>
    ///     A server handshake with a certificate fresh from the loader must succeed. On Windows
    ///     Schannel cannot use the ephemeral key that CertificateRequest.CreateSelfSigned returns,
    ///     so the first start would fail every TLS transport; the loader returns the certificate
    ///     it reloaded from the saved PFX instead. Uses real loopback sockets and a client that
    ///     accepts any certificate.
    /// </summary>
    [Fact]
    public async Task TheGeneratedSelfSignedCertificate_CompletesAServerHandshake()
    {
        string statePath = Path.Combine(_directory, "tls", "self-signed.pfx");
        TlsCertificateOptions options = new() { SelfSignedCertificateStatePath = statePath };
        using var certificate = TlsCertificateLoader.LoadOrGenerate(options, NullLogger.Instance);

        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));

        Task server = Task.Run(
            async () =>
            {
                using TcpClient accepted = await listener.AcceptTcpClientAsync(timeout.Token);
                using SslStream tls = new(accepted.GetStream());
                await tls.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions { ServerCertificate = certificate },
                    timeout.Token
                );
            },
            timeout.Token
        );

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        using SslStream clientTls = new(client.GetStream(), false, (_, _, _, _) => true);
        await clientTls.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = "localhost" },
            timeout.Token
        );

        await server;
        Assert.True(clientTls.IsAuthenticated);
    }
}
