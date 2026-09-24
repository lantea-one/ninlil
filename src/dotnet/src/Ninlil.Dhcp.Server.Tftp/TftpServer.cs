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
///     This class is Ninlil's real RFC 1350 TFTP server -- the "bundled read-only TFTP server"
///     half of this project's own dnsmasq feature-checklist research finding for Phase 14
///     (delivering a PXE client's own boot file, most commonly), matching its scope exactly:
///     read requests only. A Write Request always receives an Access Violation error; this
///     server never accepts an upload, and never needs write access to its own configured
///     <see cref="TftpServerOptions.RootDirectory" /> at all.
///     <para>
///         <b>Architecture</b>: mirrors <c>Ninlil.Dhcp.Protocol.Dhcpv4.Dhcpv4UdpListener</c>'s own
///         shape for the one initial receive loop (a single socket bound to the configured
///         listening port, gated by its own <see cref="TftpServerOptions.Enable" /> flag) but
///         diverges from it after that: RFC 1350 Section 4's own transfer-ID mechanism has every
///         individual file transfer proceed over its own separate, freshly-bound, OS-assigned
///         ephemeral UDP socket, entirely independent of the main listening socket and of every
///         other concurrent transfer's own socket -- so each Read Request this server accepts
///         spawns its own detached transfer task with its own socket, never sharing one.
///     </para>
///     <para>
///         <b>Only octet (binary) transfer mode is supported</b> -- a Read Request naming
///         <c>netascii</c> or <c>mail</c> is rejected with an Illegal Operation error. A boot
///         file (or any other file this server plausibly serves) is always binary; RFC 1350's own
///         <c>netascii</c> mode exists for portable *text* file transfer between hosts with
///         different native newline conventions, a need this server's own real-world use case
///         never has, so implementing that translation would be real, untested complexity this
///         project's own scope discipline does not justify adding.
///     </para>
///     <para>
///         <b>Path confinement</b>: <see cref="TryResolveRequestedFile" /> combines a request's
///         own filename with the configured root directory and re-resolves the result via
///         <see cref="Path.GetFullPath(string)" />, rejecting outright any request naming an
///         absolute path or whose resolved path does not fall strictly within that root -- the
///         one and only defensive check a read-only file server serving arbitrary client-supplied
///         paths absolutely cannot skip.
///     </para>
///     <para>
///         <b>This class is a genuinely restartable <see cref="IDhcpProtocolService" /></b>, not a
///         <see cref="Microsoft.Extensions.Hosting.BackgroundService" /> -- <see cref="StartAsync" />/
///         <see cref="StopAsync" /> can be called repeatedly, in either order, for the rest of this
///         process's life, each genuinely binding/releasing the main listening socket. It is
///         registered as an ordinary dependency-injection singleton (never as an
///         <see cref="Microsoft.Extensions.Hosting.IHostedService" /> itself) --
///         <c>TftpServerHostedService</c> is the thin wrapper that starts this instance once,
///         automatically, at host boot, only when <see cref="TftpServerOptions.Enable" /> is
///         <see langword="true" />. <see cref="TftpServerOptions.RootDirectory" /> being blank or
///         not actually existing is deliberately <b>not</b> a genuine start failure -- exactly as
///         before this class became independently restartable, that precondition simply declines
///         to bind (logged, <see cref="IsListening" /> stays <see langword="false" />,
///         <see cref="StartAsync" /> returns normally) rather than throwing, the same "a real
///         precondition is missing, so don't pretend to be running" posture
///         <c>TransportListenersHealthCheck</c>'s own doc comment already documents for an
///         analogous case elsewhere in this project (a transport that is enabled but declined to
///         bind for a reason of its own) -- letting this one specific, plausibly-transient
///         misconfiguration throw would crash this process's own startup outright whenever the
///         thin hosted-service wrapper's boot-time auto-start calls this method with
///         <see cref="TftpServerOptions.Enable" /> <see langword="true" /> but
///         <see cref="TftpServerOptions.RootDirectory" /> not yet valid. A genuine socket-level
///         bind failure (the configured port already in use, for instance) still propagates as a
///         real exception, exactly like every other transport in this project.
///     </para>
///     <para>
///         <b>Design decision: a live <see cref="StopAsync" /> call aborts every in-flight transfer
///         immediately, rather than letting it run to completion first.</b> This server's own real
///         transfers (PXE boot files, most commonly) are short-lived, and
///         <see cref="IDhcpProtocolService.StopAsync" />'s own documented contract is that it
///         "returns only once ... so a caller ... can rely on the underlying port already being
///         free" -- an operator-facing live stop that could instead block indefinitely behind an
///         arbitrarily slow or stalled client's own transfer would violate that same deterministic
///         contract. Every in-flight transfer this run dispatched shares this run's own
///         cancellation token (through the same <c>stoppingToken</c> parameter
///         <see cref="HandleRequestAsync" />/<see cref="TransferFileAsync" />/
///         <see cref="SendWithRetriesAsync" /> already threaded through before this class became
///         restartable), so cancelling it on <see cref="StopAsync" /> unblocks every transfer's own
///         pending send/receive promptly and each one's own <c>using</c>-scoped per-transfer socket
///         is released as that transfer's own task unwinds; <see cref="StopAsync" /> itself still
///         waits for every such task to genuinely finish before returning, so no transfer is ever
///         left running, or its socket left open, after a live stop completes.
///     </para>
/// </summary>
/// <param name="options">This server's own configured port, root directory, and retransmission policy.</param>
/// <param name="logger">The logger this server records request/transfer failures to.</param>
public sealed class TftpServer(IOptions<TftpServerOptions> options, ILogger<TftpServer> logger)
    : IDhcpProtocolService
{
    /// <summary>
    ///     This constant contains RFC 1350 Section 5's own per-block data size -- every Data
    ///     packet this server sends carries exactly this many bytes, except the very last one of
    ///     a transfer, which carries fewer (or zero, for a file whose length is an exact multiple
    ///     of this) to signal that the transfer is complete.
    /// </summary>
    private const int BlockSize = 512;

    /// <summary>
    ///     This constant contains the largest datagram this server ever expects to receive as an
    ///     incoming Read/Write Request, Acknowledgment, or Error -- generously larger than any of
    ///     those three genuinely need (an Acknowledgment is 4 bytes; even a request with an
    ///     implausibly long filename fits comfortably).
    /// </summary>
    private const int MaximumControlDatagramSize = 1024;

    /// <summary>
    ///     This lock serializes every <see cref="StartAsync" />/<see cref="StopAsync" /> call
    ///     against each other -- the same reasoning
    ///     <c>Ninlil.Dhcp.Protocol.Dhcpv6.Dhcpv6UdpListener._lifecycleLock</c>'s own doc comment
    ///     gives.
    /// </summary>
    private readonly Lock _lifecycleLock = new();

    /// <summary>
    ///     This field contains every in-flight transfer task this run's own receive loop has
    ///     dispatched (keyed by the task itself, the value unused) -- tracked so
    ///     <see cref="StopAsync" /> can genuinely wait for every one of them to finish (they all
    ///     share this run's own cancellation token, so this resolves promptly once cancelled) --
    ///     see this class's own doc comment for why a live stop aborts an in-flight transfer
    ///     rather than letting it run to completion.
    /// </summary>
    private readonly ConcurrentDictionary<Task, byte> _activeTransferTasks = new();

    private Socket _socket;

    /// <summary>
    ///     This field contains the cancellation source that owns the currently running receive
    ///     loop's own lifetime (and, through the same token, every in-flight transfer task it has
    ///     dispatched), cancelled by <see cref="StopAsync" /> to unblock the loop's own pending
    ///     receive call and every transfer's own pending send/receive, signalling all of them to
    ///     exit.
    /// </summary>
    private CancellationTokenSource _receiveLoopCts;

    /// <summary>
    ///     This field contains the currently running receive loop's own task, awaited by
    ///     <see cref="StopAsync" /> to confirm the loop has genuinely exited (and, with it, that no
    ///     further use of <see cref="_socket" /> is still in flight) before that socket is
    ///     disposed.
    /// </summary>
    private Task _receiveLoopTask;

    /// <summary>
    ///     This property reports whether this server's own listening socket is currently bound
    ///     and accepting requests.
    /// </summary>
    public bool IsListening { get; private set; }

    /// <inheritdoc />
    bool IDhcpProtocolService.IsRunning => IsListening;

    /// <summary>
    ///     This property contains the port this server's own listening socket actually bound to
    ///     -- the same as <see cref="TftpServerOptions.Port" /> in production, but lets a test
    ///     configure <c>Port = 0</c> (an OS-assigned ephemeral port) and read back which one it
    ///     actually got.
    /// </summary>
    public int BoundPort { get; private set; }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        Socket socket;
        CancellationTokenSource receiveLoopCts;

        lock (_lifecycleLock)
        {
            // Calling Start while already running is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
            if (IsListening)
                return Task.CompletedTask;

            // A blank/nonexistent RootDirectory is a real, plausibly-transient precondition
            // failure, deliberately not a thrown exception -- see this class's own doc comment
            // for why.
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
                // A genuine bind failure (the port already in use, for instance) must leave this
                // service reporting IsListening = false, not half-started -- propagate the
                // exception to the caller rather than swallowing it.
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
            // Calling Stop while already stopped is a safe, harmless no-op -- see this class's
            // own doc comment and IDhcpProtocolService's.
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

        // Cancel the loop's (and, through the same token, every in-flight transfer's) own
        // cancellation source and close the main listening socket -- closing it is what actually
        // unblocks a pending ReceiveFromAsync call on it.
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
                // Either this call's own cancellationToken fired while waiting, or the loop task
                // itself completed via cancellation -- either way, the loop has stopped touching
                // the main socket by the time this method returns.
            }
        }

        // Wait for every in-flight transfer this run dispatched to genuinely finish -- see this
        // class's own doc comment for why a live stop aborts them (via the same cancelled token)
        // rather than letting them run to completion, and why this method still waits for that
        // abort to actually land before returning.
        Task[] outstandingTransfers = [.. _activeTransferTasks.Keys];

        if (outstandingTransfers.Length > 0)
        {
            try
            {
                await Task.WhenAll(outstandingTransfers).WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Same reasoning as above -- every transfer shares the same cancelled token, so
                // this resolves promptly regardless.
            }
        }

        receiveLoopCts.Dispose();
        socket.Dispose();

        logger.LogInformation("The TFTP server stopped and released its bound socket.");
    }

    /// <summary>
    ///     This method runs one start/stop cycle's own receive loop -- mirroring
    ///     <c>Dhcpv6UdpListener.RunReceiveLoopAsync</c>'s own "pass every socket/cancellation value
    ///     explicitly rather than read it back off this instance's own fields" discipline, so a
    ///     stale field from a previous cycle can never leak into a currently running loop. Every
    ///     accepted request is dispatched as its own detached, tracked transfer task (see
    ///     <see cref="_activeTransferTasks" />) -- this loop must never block on one client's own,
    ///     possibly slow or unresponsive, transfer.
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

            // Every request gets its own detached, tracked transfer task, per this class's own
            // doc comment -- the main receive loop above must never block on one client's own,
            // possibly slow or unresponsive, transfer, but StopAsync must still be able to wait
            // for every one of them to genuinely finish.
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
    ///     This method handles a single decoded Read/Write Request end to end: validating the
    ///     request itself, then (for a Read Request naming a real, in-root file) transferring
    ///     that file's own content over a fresh, per-transfer socket. Every exception (other than
    ///     cancellation) is caught and logged -- one client's own malformed request or failed
    ///     transfer must never affect any other.
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
            // A live StopAsync call cancelled this transfer's own shared token -- see this
            // class's own doc comment for why a live stop aborts an in-flight transfer rather
            // than letting it run to completion. This is caught here, rather than left to
            // propagate out of this method's own detached task, so that task always completes
            // successfully (never faulted) regardless of how far the transfer had gotten.
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
    ///     This method transfers one file's own content, one 512-byte block at a time, over an
    ///     already-bound per-transfer socket, per RFC 1350 Section 2's own "lock-step"
    ///     data/acknowledgment exchange.
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
    ///     This method sends one Data block, waiting for its own matching Acknowledgment,
    ///     retransmitting on a timeout up to <see cref="TftpServerOptions.MaxRetransmissions" />
    ///     times.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> if the client acknowledged this exact block;
    ///     <see langword="false" /> if every retransmission attempt timed out, or the client sent
    ///     an Error packet aborting the transfer.
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

                    // A packet from anyone other than this exact transfer's own client is a
                    // stray, unrelated datagram -- keep waiting within this same attempt's
                    // timeout window rather than treating it as this block's own answer.
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

                        // A stale acknowledgment for an earlier block (a duplicate the client
                        // re-sent, most plausibly) -- keep waiting for the right one.
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

                    // Anything else is not a packet this transfer cares about -- keep waiting.
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // This attempt's own retransmit timeout elapsed -- fall through and resend.
            }
        }

        return false;
    }

    /// <summary>
    ///     This method sends an Error packet, logging (rather than throwing) if the send itself
    ///     fails.
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
    ///     This method reads up to <see cref="BlockSize" /> bytes from a file stream, looping
    ///     until either the buffer is full or the stream is exhausted -- a single
    ///     <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)" /> call is not guaranteed
    ///     to fill its whole buffer even when more data remains.
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
    ///     This method resolves a request's own filename against the configured root directory,
    ///     rejecting an absolute path outright and re-confirming (via
    ///     <see cref="Path.GetFullPath(string)" />) that the combined, resolved path still falls
    ///     strictly within that root -- see this class's own doc comment for why this check can
    ///     never be skipped.
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
