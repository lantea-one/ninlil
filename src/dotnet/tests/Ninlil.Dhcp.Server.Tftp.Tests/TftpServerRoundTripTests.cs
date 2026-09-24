using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp.Tests;

/// <summary>
///     This test proves the real <see cref="TftpServer" /> against real UDP sockets end to end:
///     a genuine multi-block file transfer, a Write Request rejection, a non-octet mode
///     rejection, a file-not-found error, and a path-traversal rejection -- the same "prove it
///     with real sockets, on a dynamically-chosen unprivileged port" discipline
///     <c>Ninlil.Dhcp.Protocol.Dhcpv4.Tests</c> already establishes for the DHCPv4 transport
///     (this server's own real, privileged, well-known port -- 69 -- is proven bindable
///     separately, the same way DHCPv4's own ports 67/68 were, via a throwaway root-run
///     scratchpad rather than a committed test, since an automated test suite does not run as
///     root).
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

        // Wait for the server's own real socket bind to complete before any test sends it a
        // request.
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
        // 1500 bytes spans three 512-byte blocks (512 + 512 + 476), proving this server's own
        // multi-block lock-step loop, not just the single-block special case.
        byte[] content = new byte[1500];
        Random.Shared.NextBytes(content);
        File.WriteAllBytes(Path.Combine(_rootDirectory, "large.bin"), content);

        byte[] received = await DownloadAsync("large.bin");

        Assert.Equal(content, received);
    }

    [Fact]
    public async Task ReadRequest_ForAFileExactlyOneBlockLong_SendsATrailingEmptyFinalBlock()
    {
        // Exactly 512 bytes: RFC 1350 requires a final, empty Data block to signal end of
        // transfer, since a full 512-byte block is otherwise ambiguous with "more data follows".
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
        // A sibling file genuinely outside the configured root directory.
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
    ///     This method drives a complete, real RRQ-DATA-ACK exchange for one file, from a fresh
    ///     client socket, and returns the file's own reassembled content.
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

            // Every block transfer arrives from the server's own fresh, per-transfer ephemeral
            // port, per RFC 1350 §4 -- this test's own client socket must follow it there for
            // every subsequent Acknowledgment, not keep addressing the original listening port.
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
