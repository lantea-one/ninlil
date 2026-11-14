using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This test class checks on Windows that <see cref="TftpServer" /> claims its UDP port
///     exclusively, so no other socket can share the TFTP port. It starts the server on an
///     OS-assigned port and tries the combinations Windows allows without exclusive use (a
///     specific address beside the wildcard, and a wildcard with SO_REUSEADDR; Winsock
///     documentation "Using SO_REUSEADDR and SO_EXCLUSIVEADDRUSE"). Both are refused. The test
///     returns at once on other systems.
/// </summary>
public sealed class TftpServerExclusivePortTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-exclusive-{Guid.NewGuid()}"
    );

    /// <inheritdoc />
    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_rootDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DisposeAsync()
    {
        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);

        return Task.CompletedTask;
    }

    [Fact]
    public async Task StartAsync_OnWindows_RefusesEverySecondBindOfThePort()
    {
        if (!OperatingSystem.IsWindows())
            return;

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = 0,
            RootDirectory = _rootDirectory,
        };
        TftpServer server = new(Options.Create(options), new CapturingLogger<TftpServer>());

        await server.StartAsync(CancellationToken.None);

        try
        {
            Assert.True(server.IsListening);

            using Socket specific = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            Assert.Throws<SocketException>(() =>
                specific.Bind(new IPEndPoint(IPAddress.Loopback, server.BoundPort))
            );

            using Socket reusing = new(
                AddressFamily.InterNetwork,
                SocketType.Dgram,
                ProtocolType.Udp
            );
            reusing.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            Assert.Throws<SocketException>(() =>
                reusing.Bind(new IPEndPoint(IPAddress.Any, server.BoundPort))
            );
        }
        finally
        {
            await server.StopAsync(CancellationToken.None);
        }
    }
}
