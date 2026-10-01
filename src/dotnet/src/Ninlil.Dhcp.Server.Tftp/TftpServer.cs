using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dhcp.Common.Control;
using Ninlil.Dhcp.Server.Tftp.Wire;

// Define our namespace.
namespace Ninlil.Dhcp.Server.Tftp;

/// <summary>
///     This class is Ninlil's read-only TFTP server (RFC 1350), mainly for delivering PXE boot
///     files. It serves Read Requests only; a Write Request gets an Access Violation error, so the
///     server never needs write access to <see cref="TftpServerOptions.RootDirectory" />.
///     <para>
///         <b>Sockets.</b> One socket on <see cref="TftpServerOptions.Port" /> receives requests,
///         as in <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>. Under RFC 1350's transfer
///         ID scheme (Section 4), each transfer then runs on a new socket with an ephemeral port,
///         so every accepted request gets a separate socket and background task.
///     </para>
///     <para>
///         <b>Octet mode only.</b> A request for <c>netascii</c> or <c>mail</c> gets an Illegal
///         Operation error. Boot files are binary, and <c>netascii</c> exists only to translate
///         text line endings between hosts. TFTP options (RFC 2347, such as <c>blksize</c> and
///         <c>tsize</c>) are ignored, so every transfer uses 512-byte blocks.
///     </para>
///     <para>
///         <b>Path confinement.</b> <see cref="TryResolveRequestedFile" /> rejects absolute paths,
///         combines the filename with the root directory, normalizes the result with
///         <see cref="Path.GetFullPath(string)" /> and rejects anything outside the root. The check
///         works on the path text only; symbolic links inside the root are followed.
///     </para>
///     <para>
///         <b>Restartable.</b> The class is an <see cref="IDhcpProtocolService" />:
///         <see cref="StartAsync" /> and <see cref="StopAsync" /> can be called any number of
///         times, binding and releasing the listening socket each time. It is a plain singleton;
///         <c>TftpServerHostedService</c> starts it at boot when
///         <see cref="TftpServerOptions.Enable" /> is set. A blank or missing
///         <see cref="TftpServerOptions.RootDirectory" /> is not an error: the server logs a
///         warning, does not bind, and leaves <see cref="IsListening" /> false, so a bad path
///         does not stop the host from booting. A failed bind, such as the port being in use,
///         throws.
///     </para>
///     <para>
///         <b>Stopping aborts transfers.</b> <see cref="IDhcpProtocolService.StopAsync" /> promises
///         that the port is free when it returns, so it cannot wait for a slow client. All
///         transfers share the run's cancellation token. <see cref="StopAsync" /> cancels it,
///         which unblocks every pending send and receive, and then waits for each transfer task to
///         finish and release its socket.
///     </para>
/// </summary>
/// <param name="options">The port, root directory and retransmission settings.</param>
/// <param name="logger">The logger request and transfer failures are recorded to.</param>
public sealed class TftpServer(IOptions<TftpServerOptions> options, ILogger<TftpServer> logger)
    : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains the RFC 1350 data block size. Every Data packet carries this
    ///     many bytes except the last, which carries fewer (zero when the file length is a
    ///     multiple of 512) to mark the end of the transfer.
    /// </summary>
    private const int BlockSize = 512;

    /// <summary>
    ///     This constant contains the receive buffer size for requests, Acknowledgments and
    ///     Errors. An Acknowledgment is 4 bytes and a request is a filename and mode, so 1024 is
    ///     ample; a longer datagram is truncated.
    /// </summary>
    private const int MaximumControlDatagramSize = 1024;

    /// <summary>
    ///     This lock serializes <see cref="StartAsync" /> and <see cref="StopAsync" />, as in
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener._lifecycleLock</c>.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains the in-flight transfer tasks, used as a set (the value is unused),
    ///     so <see cref="StopAsync" /> can wait for them after cancelling them.
    /// </summary>
    private readonly ConcurrentDictionary<Task, byte> _activeTransferTasks = new();

    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source for the current run. Its token is passed to
    ///     the receive loop and every transfer, and <see cref="StopAsync" /> cancels it to stop
    ///     them all.
    /// </summary>
    private CancellationTokenSource _receiveLoopCts;

    /// <summary>
    ///     This field contains the receive loop task. <see cref="StopAsync" /> awaits it so the
    ///     loop is no longer using <see cref="_socket" /> when the socket is disposed.
    /// </summary>
    private Task _receiveLoopTask;

    /// <summary>
    ///     This property reports whether the listening socket is bound and accepting requests.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    bool IDhcpProtocolService.IsRunning => IsListening;

    /// <summary>
    ///     This property contains the port the listening socket is bound to, or 0 when stopped.
    ///     It equals <see cref="TftpServerOptions.Port" /> unless that is 0, which tests use to get
    ///     an ephemeral port.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;

        lock (_lifecycleLock)
        {
            // Starting while running does nothing (see IDhcpProtocolService).
            if (IsListening)
                return Task.CompletedTask;

            // A blank or missing RootDirectory is logged, not thrown (see the class summary).
            if (string.IsNullOrWhiteSpace(options.Value.RootDirectory))
            {
                logger.LogWarning(
                    "The TFTP server is enabled but no {OptionName} is configured; it will not bind a socket.",
                    $"{TftpServerOptions.SectionName}:{nameof(TftpServerOptions.RootDirectory)}"
                );
                return Task.CompletedTask;
            }

            string rootDirectory = Path.GetFullPath(options.Value.RootDirectory);

            if (!Directory.Exists(rootDirectory))
            {
                logger.LogWarning(
                    "The TFTP server's own configured root directory '{RootDirectory}' does not exist; it will not bind a socket.",
                    rootDirectory
                );
                return Task.CompletedTask;
            }

            if (!rootDirectory.EndsWith(Path.DirectorySeparatorChar))
                rootDirectory += Path.DirectorySeparatorChar;

            socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            try
            {
                socket.SetSocketOption(
                    SocketOptionLevel.Socket,
                    SocketOptionName.ReuseAddress,
                    true
                );
                socket.Bind(new IPEndPoint(IPAddress.Any, options.Value.Port));
            }
            catch
            {
                // Leave the service stopped, not half-started, and let the caller see the error.
                socket.Dispose();
                throw;
            }

            BoundPort = ((IPEndPoint)socket.LocalEndPoint).Port;

            receiveLoopCts = new CancellationTokenSource();

            _socket = socket;
            _receiveLoopCts = receiveLoopCts;
            IsListening = true;

            logger.LogInformation(
                "The TFTP server started and is now bound to UDP port {BoundPort}.",
                BoundPort
            );

            _receiveLoopTask = Task.Run(() =>
                RunReceiveLoopAsync(socket, rootDirectory, receiveLoopCts.Token)
            );
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;
        Task receiveLoopTask;

        lock (_lifecycleLock)
        {
            // Stopping while stopped does nothing (see IDhcpProtocolService).
            if (!IsListening)
                return;

            socket = _socket;
            receiveLoopCts = _receiveLoopCts;
            receiveLoopTask = _receiveLoopTask;

            _socket = null;
            _receiveLoopCts = null;
            _receiveLoopTask = null;
            IsListening = false;
            BoundPort = 0;
        }

        // Cancel the loop and all transfers, and close the listening socket so a pending
        // ReceiveFromAsync returns.
        await receiveLoopCts.CancelAsync();
        socket.Close();

        if (receiveLoopTask is not null)
        {
            try
            {
                await receiveLoopTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Either the caller's token fired while waiting or the loop ended by
                // cancellation. Carry on with the cleanup either way.
            }
        }

        // Wait for the cancelled transfers to finish and close their sockets (see the class
        // summary).
        Task[] outstandingTransfers = [.. _activeTransferTasks.Keys];

        if (outstandingTransfers.Length > 0)
        {
            try
            {
                await Task.WhenAll(outstandingTransfers).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Only the caller's token can end the wait early; transfers catch the
                // cancellation themselves.
            }
        }

        receiveLoopCts.Dispose();
        socket.Dispose();

        logger.LogInformation("The TFTP server stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method runs the receive loop for one start/stop cycle. The socket, root directory
    ///     and token are passed in, not read from fields, so a later cycle's values cannot leak
    ///     in, as in <c>Dhcpv6UdpListener.RunReceiveLoopAsync</c>. Each datagram is handled by a
    ///     tracked background task (see <see cref="_activeTransferTasks" />), so a slow client
    ///     does not block the loop. There is no limit on concurrent transfers.
    /// </summary>
    private async Task RunReceiveLoopAsync(
        Socket socket,
        string rootDirectory,
        CancellationToken stoppingToken
    )
    {
        byte[] receiveBuffer = new byte[MaximumControlDatagramSize];

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await socket.ReceiveFromAsync(
                    receiveBuffer,
                    new IPEndPoint(IPAddress.Any, 0),
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (SocketException exception)
            {
                if (stoppingToken.IsCancellationRequested)
                    break;

                logger.LogWarning(exception, "Error receiving a TFTP datagram.");
                continue;
            }

            byte[] datagram = receiveBuffer[..result.ReceivedBytes];
            IPEndPoint clientEndPoint = (IPEndPoint)result.RemoteEndPoint;

            // Not awaited, so the loop can take the next request; tracked so StopAsync can wait.
            // HandleRequestAsync runs on this thread until its first await.
            Task transferTask = HandleRequestAsync(
                datagram,
                clientEndPoint,
                rootDirectory,
                stoppingToken
            );
            _activeTransferTasks[transferTask] = 0;
            _ = transferTask.ContinueWith(
                completed => _activeTransferTasks.TryRemove(completed, out _),
                TaskScheduler.Default
            );
        }
    }

    /// <summary>
    ///     This method handles one datagram from the listening socket. It decodes the request,
    ///     answers with an Error packet when the request is a write, uses another mode than octet,
    ///     names a path outside the root or names a missing file, and otherwise sends the file.
    ///     Every reply goes from a new per-transfer socket. All exceptions are caught and logged,
    ///     so one bad request cannot affect another.
    /// </summary>
    private async Task HandleRequestAsync(
        byte[] datagram,
        IPEndPoint clientEndPoint,
        string rootDirectory,
        CancellationToken stoppingToken
    )
    {
        using Socket transferSocket = new(
            AddressFamily.InterNetwork,
            SocketType.Dgram,
            ProtocolType.Udp
        );

        try
        {
            transferSocket.Bind(new IPEndPoint(IPAddress.Any, 0));

            if (
                !TftpPacketReader.TryReadRequest(
                    datagram,
                    out TftpRequestPacket request,
                    out string failureReason
                )
            )
            {
                logger.LogWarning(
                    "Discarding an unparseable TFTP datagram from {RemoteEndPoint}: {Reason}",
                    clientEndPoint,
                    failureReason
                );
                return;
            }

            if (request.Opcode == TftpOpcodeEnum.WriteRequest)
            {
                await SendErrorAsync(
                    transferSocket,
                    clientEndPoint,
                    TftpErrorCodeEnum.AccessViolation,
                    "This TFTP server is read-only.",
                    stoppingToken
                );
                return;
            }

            if (!string.Equals(request.Mode, "octet", StringComparison.OrdinalIgnoreCase))
            {
                await SendErrorAsync(
                    transferSocket,
                    clientEndPoint,
                    TftpErrorCodeEnum.IllegalOperation,
                    "Only octet (binary) transfer mode is supported.",
                    stoppingToken
                );
                return;
            }

            if (
                !TryResolveRequestedFile(
                    rootDirectory,
                    request.Filename,
                    out string resolvedPath,
                    out string rejectReason
                )
            )
            {
                logger.LogWarning(
                    "Rejecting a TFTP request from {RemoteEndPoint} for '{Filename}': {Reason}",
                    clientEndPoint,
                    request.Filename,
                    rejectReason
                );
                await SendErrorAsync(
                    transferSocket,
                    clientEndPoint,
                    TftpErrorCodeEnum.AccessViolation,
                    rejectReason,
                    stoppingToken
                );
                return;
            }

            if (!File.Exists(resolvedPath))
            {
                await SendErrorAsync(
                    transferSocket,
                    clientEndPoint,
                    TftpErrorCodeEnum.FileNotFound,
                    $"'{request.Filename}' does not exist.",
                    stoppingToken
                );
                return;
            }

            await TransferFileAsync(transferSocket, clientEndPoint, resolvedPath, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // StopAsync cancelled the run (see the class summary). Catching it here means the
            // task always completes normally instead of ending cancelled.
            logger.LogDebug(
                "Aborted an in-flight TFTP transfer to {RemoteEndPoint}: the TFTP server was stopped.",
                clientEndPoint
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unhandled exception while handling a TFTP request from {RemoteEndPoint}.",
                clientEndPoint
            );
        }
    }

    /// <summary>
    ///     This method sends a file in 512-byte blocks over the per-transfer socket, waiting for
    ///     each block to be acknowledged before sending the next (the lock-step exchange of RFC
    ///     1350). Block numbers start at 1 and wrap from 65535 to 0 on files over about 32 MB.
    /// </summary>
    private async Task TransferFileAsync(
        Socket transferSocket,
        IPEndPoint clientEndPoint,
        string path,
        CancellationToken stoppingToken
    )
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] blockBuffer = new byte[BlockSize];
        ushort blockNumber = 1;
        int bytesRead;

        do
        {
            bytesRead = await ReadFullBlockAsync(stream, blockBuffer, stoppingToken);
            byte[] dataPacket = TftpPacketWriter.WriteData(
                blockNumber,
                blockBuffer.AsSpan(0, bytesRead)
            );

            bool acknowledged = await SendWithRetriesAsync(
                transferSocket,
                clientEndPoint,
                dataPacket,
                blockNumber,
                stoppingToken
            );

            if (!acknowledged)
            {
                logger.LogWarning(
                    "Aborting a TFTP transfer of '{Path}' to {RemoteEndPoint}: no acknowledgment after {MaxRetransmissions} retransmissions of block {BlockNumber}.",
                    path,
                    clientEndPoint,
                    options.Value.MaxRetransmissions,
                    blockNumber
                );
                return;
            }

            blockNumber++;
        } while (bytesRead == BlockSize);
    }

    /// <summary>
    ///     This method sends one Data block and waits for its Acknowledgment, resending after each
    ///     <see cref="TftpServerOptions.RetransmitTimeout" /> up to
    ///     <see cref="TftpServerOptions.MaxRetransmissions" /> times. Packets from other endpoints,
    ///     duplicate Acknowledgments and other packets are ignored without resending, which
    ///     avoids the Sorcerer's Apprentice problem.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the client acknowledged this block;
    ///     <see langword="false" /> if every attempt timed out or the client sent an Error
    ///     packet.
    /// </returns>
    private async Task<bool> SendWithRetriesAsync(
        Socket transferSocket,
        IPEndPoint clientEndPoint,
        byte[] dataPacket,
        ushort blockNumber,
        CancellationToken stoppingToken
    )
    {
        byte[] receiveBuffer = new byte[MaximumControlDatagramSize];

        for (int attempt = 0; attempt <= options.Value.MaxRetransmissions; attempt++)
        {
            await transferSocket.SendToAsync(dataPacket, clientEndPoint, stoppingToken);

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(options.Value.RetransmitTimeout);

            try
            {
                while (true)
                {
                    SocketReceiveFromResult result = await transferSocket.ReceiveFromAsync(
                        receiveBuffer,
                        new IPEndPoint(IPAddress.Any, 0),
                        timeoutCts.Token
                    );

                    // Not from this transfer's client: ignore it and keep waiting.
                    if (!clientEndPoint.Equals(result.RemoteEndPoint))
                        continue;

                    ReadOnlySpan<byte> received = receiveBuffer.AsSpan(0, result.ReceivedBytes);

                    if (
                        TftpPacketReader.TryReadAcknowledgment(
                            received,
                            out ushort acknowledgedBlockNumber,
                            out _
                        )
                    )
                    {
                        if (acknowledgedBlockNumber == blockNumber)
                            return true;

                        // An Acknowledgment for an earlier block, usually a duplicate.
                        continue;
                    }

                    if (
                        TftpPacketReader.TryReadError(
                            received,
                            out TftpErrorCodeEnum errorCode,
                            out string errorMessage,
                            out _
                        )
                    )
                    {
                        logger.LogWarning(
                            "A TFTP transfer to {RemoteEndPoint} was aborted by the client: {ErrorCode} {ErrorMessage}",
                            clientEndPoint,
                            errorCode,
                            errorMessage
                        );
                        return false;
                    }

                    // Anything else is ignored.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // The retransmit timeout elapsed; resend.
            }
        }

        return false;
    }

    /// <summary>
    ///     This method sends an Error packet, logging instead of throwing if the send fails.
    /// </summary>
    private async Task SendErrorAsync(
        Socket transferSocket,
        IPEndPoint clientEndPoint,
        TftpErrorCodeEnum errorCode,
        string errorMessage,
        CancellationToken stoppingToken
    )
    {
        byte[] packet = TftpPacketWriter.WriteError(errorCode, errorMessage);

        try
        {
            await transferSocket.SendToAsync(packet, clientEndPoint, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Error sending a TFTP error packet to {RemoteEndPoint}.",
                clientEndPoint
            );
        }
    }

    /// <summary>
    ///     This method fills the buffer from the stream, stopping early only at end of file. A
    ///     single <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)" /> call may return
    ///     fewer bytes even when more remain, and a short block would end the transfer.
    /// </summary>
    private static async Task<int> ReadFullBlockAsync(
        FileStream stream,
        byte[] buffer,
        CancellationToken stoppingToken
    )
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(
                buffer.AsMemory(totalRead, buffer.Length - totalRead),
                stoppingToken
            );

            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    /// <summary>
    ///     This method resolves a requested filename inside the root directory. It rejects an
    ///     empty name and an absolute path (including a leading <c>/</c>), and rejects a path that
    ///     <see cref="Path.GetFullPath(string)" /> resolves to outside the root, for example
    ///     through <c>..</c> segments. Symbolic links are not resolved.
    /// </summary>
    private static bool TryResolveRequestedFile(
        string rootDirectoryWithTrailingSeparator,
        string requestedFilename,
        out string resolvedPath,
        out string rejectReason
    )
    {
        resolvedPath = null;
        rejectReason = null;

        if (string.IsNullOrWhiteSpace(requestedFilename))
        {
            rejectReason = "No filename was given.";
            return false;
        }

        if (Path.IsPathRooted(requestedFilename))
        {
            rejectReason = "Absolute paths are not permitted.";
            return false;
        }

        string fullPath = Path.GetFullPath(
            Path.Combine(rootDirectoryWithTrailingSeparator, requestedFilename)
        );

        if (!fullPath.StartsWith(rootDirectoryWithTrailingSeparator, StringComparison.Ordinal))
        {
            rejectReason =
                "The requested path is outside this server's own configured root directory.";
            return false;
        }

        resolvedPath = fullPath;
        return true;
    }
}
