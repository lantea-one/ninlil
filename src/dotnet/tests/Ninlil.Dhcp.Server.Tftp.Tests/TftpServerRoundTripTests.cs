using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This class tests <see cref="TftpServer" /> end to end over loopback UDP: transfers of one
///     block, several blocks and exactly 512 bytes, and the errors for a missing file, a
///     <c>..</c> path, a Write Request and a non-octet mode. Each test class instance starts a
///     server on an ephemeral port with a temporary root directory. Port 69 is privileged and
///     is not used, since the tests do not run as root.
/// </summary>
public sealed class TftpServerRoundTripTests : IAsyncLifetime
{
    private readonly string _rootDirectory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-tftp-root-{Guid.NewGuid()}"
    );

    private TftpServer _server;

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_rootDirectory);

        TftpServerOptions options = new()
        {
            Enable = true,
            Port = 0,
            RootDirectory = _rootDirectory,
            RetransmitTimeout = TimeSpan.FromMilliseconds(500),
            MaxRetransmissions = 3,
        };
        _server = new TftpServer(Options.Create(options), NullLogger<TftpServer>.Instance);
        await _server.StartAsync(CancellationToken.None);

        // Make sure the server is listening before any test sends a request.
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!_server.IsListening && DateTime.UtcNow < deadline)
            await Task.Delay(10);

        Assert.True(_server.IsListening, "The TFTP server never finished binding its socket.");
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        await _server.StopAsync(CancellationToken.None);

        if (Directory.Exists(_rootDirectory))
            Directory.Delete(_rootDirectory, recursive: true);
    }

    [Fact]
    public async Task ReadRequest_ForAFileSmallerThanOneBlock_TransfersItInOneBlock()
    {
        byte[] content = "hello pxe"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_rootDirectory, "small.bin"), content);

        byte[] received = await DownloadAsync("small.bin");

        Assert.Equal(content, received);
    }

    [Fact]
    public async Task ReadRequest_ForAFileSpanningMultipleBlocks_TransfersEveryBlockInOrder()
    {
        // 1500 bytes is three blocks (512 + 512 + 476), exercising the lock-step loop.
        byte[] content = new byte[1500];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "large.bin"), content);

        byte[] received = await DownloadAsync("large.bin");

        Assert.Equal(content, received);
    }

    [Fact]
    public async Task ReadRequest_ForAFileExactlyOneBlockLong_SendsATrailingEmptyFinalBlock()
    {
        // Exactly 512 bytes: a full block means more data follows (RFC 1350), so the server must
        // send an empty final block.
        byte[] content = new byte[512];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "exact.bin"), content);

        byte[] received = await DownloadAsync("exact.bin");

        Assert.Equal(content, received);
    }

    [Fact]
    public async Task ReadRequest_ForAMissingFile_ReturnsFileNotFound()
    {
        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, _server.BoundPort);

        await client.SendAsync(BuildReadRequest("does-not-exist.bin"), serverEndPoint);

        UdpReceiveResult result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            TftpPacketReader.TryReadError(
                result.Buffer,
                out TftpErrorCodeEnum errorCode,
                out _,
                out _
            )
        );
        Assert.Equal(TftpErrorCodeEnum.FileNotFound, errorCode);
    }

    [Fact]
    public async Task ReadRequest_WithAPathTraversalAttempt_ReturnsAccessViolation()
    {
        // A file next to the root directory, outside it.
        string outsideFile = Path.Combine(Path.GetDirectoryName(_rootDirectory)!, "secret.txt");
        File.WriteAllText(outsideFile, "should never be served");

        try
        {
            using UdpClient client = new();
            IPEndPoint serverEndPoint = new(IPAddress.Loopback, _server.BoundPort);

            await client.SendAsync(BuildReadRequest("../secret.txt"), serverEndPoint);

            UdpReceiveResult result = await client
                .ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(
                TftpPacketReader.TryReadError(
                    result.Buffer,
                    out TftpErrorCodeEnum errorCode,
                    out _,
                    out _
                )
            );
            Assert.Equal(TftpErrorCodeEnum.AccessViolation, errorCode);
        }
        finally
        {
            File.Delete(outsideFile);
        }
    }

    [Fact]
    public async Task WriteRequest_IsAlwaysRejected()
    {
        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, _server.BoundPort);

        byte[] request =
        [
            0x00,
            0x02,
            .. "upload.bin"u8.ToArray(),
            0x00,
            .. "octet"u8.ToArray(),
            0x00,
        ];
        await client.SendAsync(request, serverEndPoint);

        UdpReceiveResult result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            TftpPacketReader.TryReadError(
                result.Buffer,
                out TftpErrorCodeEnum errorCode,
                out _,
                out _
            )
        );
        Assert.Equal(TftpErrorCodeEnum.AccessViolation, errorCode);
    }

    [Fact]
    public async Task ReadRequest_WithANonOctetMode_ReturnsIllegalOperation()
    {
        File.WriteAllText(Path.Combine(_rootDirectory, "text.txt"), "hello");

        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, _server.BoundPort);

        byte[] request =
        [
            0x00,
            0x01,
            .. "text.txt"u8.ToArray(),
            0x00,
            .. "netascii"u8.ToArray(),
            0x00,
        ];
        await client.SendAsync(request, serverEndPoint);

        UdpReceiveResult result = await client.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(
            TftpPacketReader.TryReadError(
                result.Buffer,
                out TftpErrorCodeEnum errorCode,
                out _,
                out _
            )
        );
        Assert.Equal(TftpErrorCodeEnum.IllegalOperation, errorCode);
    }

    /// <summary>
    ///     This method downloads one file with a minimal TFTP client on a new socket and returns
    ///     the content.
    /// </summary>
    private async Task<byte[]> DownloadAsync(string filename)
    {
        using UdpClient client = new();
        IPEndPoint serverEndPoint = new(IPAddress.Loopback, _server.BoundPort);

        await client.SendAsync(BuildReadRequest(filename), serverEndPoint);

        using MemoryStream received = new();
        ushort expectedBlockNumber = 1;

        while (true)
        {
            UdpReceiveResult result = await client
                .ReceiveAsync()
                .WaitAsync(TimeSpan.FromSeconds(5));

            // Blocks come from the per-transfer port (RFC 1350 Section 4), so acknowledge there,
            // not to the listening port.
            IPEndPoint transferEndPoint = (IPEndPoint)result.RemoteEndPoint;

            ushort blockNumber = ReadBlockNumber(result.Buffer);
            Assert.Equal(expectedBlockNumber, blockNumber);

            byte[] blockData = result.Buffer[4..];
            received.Write(blockData);

            byte[] ack = BuildAcknowledgment(blockNumber);
            await client.SendAsync(ack, transferEndPoint);

            if (blockData.Length < 512)
                break;

            expectedBlockNumber++;
        }

        return received.ToArray();
    }

    private static ushort ReadBlockNumber(byte[] dataPacket) =>
        (ushort)((dataPacket[2] << 8) | dataPacket[3]);

    private static byte[] BuildReadRequest(string filename) =>
        [
            0x00,
            0x01,
            .. System.Text.Encoding.ASCII.GetBytes(filename),
            0x00,
            .. "octet"u8.ToArray(),
            0x00,
        ];

    private static byte[] BuildAcknowledgment(ushort blockNumber) =>
        [0x00, 0x04, (byte)(blockNumber >> 8), (byte)blockNumber];
}
