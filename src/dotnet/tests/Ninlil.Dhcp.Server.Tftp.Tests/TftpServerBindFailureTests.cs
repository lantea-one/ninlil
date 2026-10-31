using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This test class checks what <see cref="TftpServerHostedService" /> does at boot when the
///     TFTP port is already taken: another socket on the wildcard UDP address holds an
///     OS-assigned port (without address reuse), so the server's bind fails with a real "address
///     in use" error. The hosted service must not throw, because an exception out of a hosted
///     service stops the whole host. It logs one Error, the server stays stopped and keeps the
///     reason for the health check.
/// </summary>
public sealed class TftpServerBindFailureTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-bind-{Guid.NewGuid()}"
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
    public async Task HostedServiceStart_WhenThePortIsTaken_LogsOneErrorAndDoesNotThrow()
    {
        using Socket occupant = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        occupant.Bind(new IPEndPoint(IPAddress.Any, 0));
        int port = ((IPEndPoint)occupant.LocalEndPoint).Port;

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = port,
            RootDirectory = _rootDirectory,
        };
        TftpServer server = new(Options.Create(options), new CapturingLogger<TftpServer>());
        CapturingLogger<TftpServerHostedService> logger = new();
        TftpServerHostedService hosted = new(server, Options.Create(options), logger);

        await hosted.StartAsync(CancellationToken.None);

        Assert.False(server.IsListening);
        Assert.Contains("in use", server.StartFailure, StringComparison.OrdinalIgnoreCase);
        string error = Assert.Single(logger.Messages(LogLevel.Error));
        Assert.Contains("could not bind its socket", error);
        Assert.Contains("in use", error, StringComparison.OrdinalIgnoreCase);
    }
}
