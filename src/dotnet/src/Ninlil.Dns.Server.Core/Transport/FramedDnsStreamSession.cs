using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Core.Pipeline;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Transport;

/// <summary>
///     This class serves one connection of a stream transport that frames DNS messages with a
///     two-octet big-endian length prefix (RFC 1035 §4.2.2, RFC 7766). Plain TCP and DNSCrypt over
///     TCP share it, so the framing, the per-connection limits and the teardown rules live in one
///     place. A listener accepts the connection, wraps it in a stream and calls
///     <see cref="RunAsync" /> with a handler for each received message.
///     <para>
///         <b>One reader, one writer.</b> <see cref="RunAsync" /> reads frames and calls the
///         handler. Replies never go straight to the socket from the pipeline. The delegate from
///         <see cref="CreateResponder" /> only places a frame on a bounded queue and returns, and a
///         writer task of the session writes the queue in order. A client that stops reading
///         therefore holds a queue entry, not a
///         <see cref="DnsQueryProcessingPipeline" /> slot.
///     </para>
///     <para>
///         <b>Limits.</b> At most <see cref="MaxInFlightQueriesPerConnection" /> queries are in
///         flight per connection (BIND 9 allows 23 pipelined queries). A query counts from the
///         moment its frame is read until its reply has been written, dropped or discarded. At the
///         limit the session stops reading, which pushes back through TCP flow control. Every
///         write must finish within the idle timeout. A write that misses it aborts the connection,
///         which frees all of its slots. Reading is also limited by the idle timeout, per frame.
///     </para>
///     <para>
///         <b>Idle, EOF and shutdown.</b> RFC 7766 §6.2.3 says a connection with queries
///         outstanding is not idle. When the read side goes idle or the client closes it, the
///         session stops reading, waits for the in-flight queries (each reply is bounded by the
///         write deadline), flushes the queue and returns. An abort or the stopping token ends it at
///         once: queued frames are discarded and every slot is released.
///     </para>
///     <para>
///         <b>Replies that cannot be queued.</b> A reply is discarded, and counted in
///         <see cref="DroppedResponseCount" />, when the queue is full, the connection is already
///         closed, or the reply is longer than a frame can carry (65 535 octets). The slot is
///         released. The queue holds more frames than there are slots, so a full queue is possible
///         only while a zone transfer is filling it.
///     </para>
///     <para>
///         <b>Zone transfers.</b> A transfer runs inside the handler on <see cref="Lifetime" /> and
///         not on the read-idle token, bounded by <see cref="MaxTransferDuration" /> (BIND's
///         <c>max-transfer-time-out</c>), and sends its messages with <see cref="SendAsync" />. A
///         secondary that stops reading is cut by the write deadline.
///     </para>
/// </summary>
public sealed class FramedDnsStreamSession
{
    /// <summary>
    ///     This constant contains how many queries one connection may have in flight at once. It
    ///     is below BIND 9's limit of 23.
    /// </summary>
    public const int MaxInFlightQueriesPerConnection = 16;

    /// <summary>
    ///     This constant contains how many reply frames may wait for the writer. It exceeds
    ///     <see cref="MaxInFlightQueriesPerConnection" /> so that query replies always fit.
    /// </summary>
    public const int OutboundQueueCapacity = 32;

    /// <summary>
    ///     This field contains how long one zone transfer may run, after BIND's
    ///     <c>max-transfer-time-out</c> of 120 minutes. The idle timeout does not apply to a
    ///     transfer.
    /// </summary>
    public static readonly TimeSpan MaxTransferDuration = TimeSpan.FromHours(2);

    /// <summary>
    ///     This constant contains the largest message a two-octet length prefix can describe.
    /// </summary>
    private const int MaxFrameLength = ushort.MaxValue;

    private readonly Stream _stream;
    private readonly IPAddress _remoteAddress;
    private readonly TimeSpan _idleTimeout;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _lifetimeCts;
    private readonly SemaphoreSlim _slots = new(
        MaxInFlightQueriesPerConnection,
        MaxInFlightQueriesPerConnection
    );
    private readonly Channel<OutboundFrame> _outbound = Channel.CreateBounded<OutboundFrame>(
        new BoundedChannelOptions(OutboundQueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        }
    );
    private readonly ConcurrentDictionary<QueryTicket, byte> _tickets = new();

    /// <summary>
    ///     This field contains the ticket of the message the handler is processing. Only the
    ///     read loop sets it, and the handler runs inside that loop, so
    ///     <see cref="CreateResponder" /> and <see cref="CreateDropNotifier" /> bind to the right
    ///     query.
    /// </summary>
    private QueryTicket _currentTicket;

    private long _droppedResponses;

    /// <summary>
    ///     This constructor prepares a session; nothing is read or written until
    ///     <see cref="RunAsync" /> runs.
    /// </summary>
    /// <param name="stream">The connection. The session never closes or disposes it.</param>
    /// <param name="remoteAddress">The client's address, for log messages.</param>
    /// <param name="idleTimeout">
    ///     How long to wait for a frame, and how long one write may take.
    /// </param>
    /// <param name="logger">The logger connection-level events are written to.</param>
    /// <param name="stoppingToken">The token that ends the session at host shutdown.</param>
    public FramedDnsStreamSession(
        Stream stream,
        IPAddress remoteAddress,
        TimeSpan idleTimeout,
        ILogger logger,
        CancellationToken stoppingToken
    )
    {
        _stream = stream;
        _remoteAddress = remoteAddress;
        _idleTimeout = idleTimeout;
        _logger = logger;
        _lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Lifetime = _lifetimeCts.Token;
    }

    /// <summary>
    ///     This property contains a token that is cancelled when the session is aborted or the
    ///     host stops. Work done for the connection, such as a zone transfer, runs on it.
    /// </summary>
    public CancellationToken Lifetime { get; }

    /// <summary>
    ///     This property contains how many queries are in flight now, for tests and diagnostics.
    /// </summary>
    public int InFlightQueries => _tickets.Count;

    /// <summary>
    ///     This property contains how many replies were discarded instead of queued; see the
    ///     class summary for when that happens.
    /// </summary>
    public long DroppedResponseCount => Interlocked.Read(ref _droppedResponses);

    /// <summary>
    ///     This method ends the connection now. Queued frames are discarded, in-flight slots are
    ///     released as their queries finish, and <see cref="RunAsync" /> returns. It is safe to
    ///     call more than once, from any thread.
    /// </summary>
    public void Abort()
    {
        _outbound.Writer.TryComplete();

        try
        {
            _lifetimeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The session already finished.
        }
    }

    /// <summary>
    ///     This method returns the delegate that sends a reply for the message the handler is
    ///     processing. It queues the frame and returns at once, so a pipeline slot never waits on
    ///     a socket. The slot of the message is released when the frame has been written, or when
    ///     it is discarded.
    /// </summary>
    public DnsResponseSenderAsync CreateResponder()
    {
        QueryTicket ticket = CurrentTicket();

        return (responseBytes, _) =>
        {
            QueueQueryResponse(ticket, responseBytes.Span);
            return ValueTask.CompletedTask;
        };
    }

    /// <summary>
    ///     This method returns the delegate the pipeline calls when it ends the message the
    ///     handler is processing without a reply. It releases the message's slot.
    /// </summary>
    public DnsQueryDropNotifier CreateDropNotifier()
    {
        QueryTicket ticket = CurrentTicket();
        return _ => ticket.Release();
    }

    /// <summary>
    ///     This method queues one frame that is not tied to a query slot, such as a message of a
    ///     zone transfer, and waits while the queue is full. The wait ends when the writer makes
    ///     room, the token is cancelled, or the connection closes.
    /// </summary>
    /// <exception cref="IOException">The connection is closed.</exception>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> messageBytes, CancellationToken token)
    {
        try
        {
            await _outbound.Writer.WriteAsync(
                new OutboundFrame(BuildFrame(messageBytes.Span), null),
                token
            );
        }
        catch (ChannelClosedException)
        {
            throw new IOException("The connection is closed.");
        }
    }

    /// <summary>
    ///     This method serves the connection until it ends: it reads each frame, calls
    ///     <paramref name="handleFrameAsync" />, and returns after the queue is flushed or the
    ///     connection is aborted. It does not throw for an ordinary disconnect or a stalled client.
    /// </summary>
    /// <param name="handleFrameAsync">
    ///     The handler for one received message. It gets the message bytes, this session and
    ///     <see cref="Lifetime" />, and returns what the session does next (see
    ///     <see cref="FrameOutcomeEnum" />). It runs inside the read loop, so it must not wait for
    ///     socket writes of ordinary queries.
    /// </param>
    public async Task RunAsync(
        Func<
            byte[],
            FramedDnsStreamSession,
            CancellationToken,
            Task<FrameOutcomeEnum>
        > handleFrameAsync
    )
    {
        Task writer = Task.Run(WriteLoopAsync);

        try
        {
            if (await ReadLoopAsync(handleFrameAsync))
                await WaitForInFlightQueriesAsync();
        }
        catch (OperationCanceledException)
        {
            // Aborted or stopping.
        }
        catch (Exception exception)
        {
            _logger.LogDebug(
                exception,
                "Stream connection from {Endpoint} ended abnormally.",
                _remoteAddress
            );
            Abort();
        }
        finally
        {
            _outbound.Writer.TryComplete();
            await writer;

            // Release any slot still held by a query the pipeline has not finished, so none leaks.
            foreach (QueryTicket ticket in _tickets.Keys)
                ticket.Release();

            _lifetimeCts.Dispose();
        }
    }

    /// <summary>
    ///     This method reads frames until the connection goes idle or closes, or the handler asks
    ///     to close.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> when the session should wait for in-flight queries and flush;
    ///     the method throws for an abort.
    /// </returns>
    private async Task<bool> ReadLoopAsync(
        Func<
            byte[],
            FramedDnsStreamSession,
            CancellationToken,
            Task<FrameOutcomeEnum>
        > handleFrameAsync
    )
    {
        using CancellationTokenSource idleCts = CancellationTokenSource.CreateLinkedTokenSource(
            Lifetime
        );

        while (true)
        {
            // At the limit, stop reading until a query completes: back-pressure, not an error.
            await _slots.WaitAsync(Lifetime);

            QueryTicket ticket = new(this);

            try
            {
                idleCts.CancelAfter(_idleTimeout);
                byte[] message;

                try
                {
                    byte[] lengthPrefix = new byte[2];

                    if (!await ReadExactAsync(lengthPrefix, idleCts.Token))
                    {
                        ticket.Release();
                        return true;
                    }

                    message = new byte[BinaryPrimitives.ReadUInt16BigEndian(lengthPrefix)];

                    if (!await ReadExactAsync(message, idleCts.Token))
                        throw new IOException("The connection closed partway through a frame.");
                }
                catch (OperationCanceledException) when (!Lifetime.IsCancellationRequested)
                {
                    // The idle timeout, not an abort: stop reading, answer what is outstanding.
                    ticket.Release();
                    return true;
                }

                idleCts.CancelAfter(Timeout.InfiniteTimeSpan);
                _tickets.TryAdd(ticket, 0);
                _currentTicket = ticket;

                FrameOutcomeEnum outcome = await handleFrameAsync(message, this, Lifetime);

                if (outcome != FrameOutcomeEnum.Enqueued)
                    ticket.Release();

                if (outcome == FrameOutcomeEnum.CloseAfterDrain)
                    return true;
            }
            catch
            {
                // Nothing will answer a message the handler did not accept.
                ticket.Release();
                throw;
            }
        }
    }

    /// <summary>
    ///     This method waits until no query is in flight, by taking every slot. Each reply is
    ///     bounded by the write deadline, so the wait is too, unless the pipeline never reports
    ///     a query (it always reports one).
    /// </summary>
    private async Task WaitForInFlightQueriesAsync()
    {
        for (int taken = 0; taken < MaxInFlightQueriesPerConnection; taken++)
            await _slots.WaitAsync(Lifetime);
    }

    /// <summary>
    ///     This method writes the outbound queue, one frame at a time, each within the idle
    ///     timeout. A failed or late write aborts the session; the loop then discards what is
    ///     left. It never throws.
    /// </summary>
    private async Task WriteLoopAsync()
    {
        while (await _outbound.Reader.WaitToReadAsync())
        {
            while (_outbound.Reader.TryRead(out OutboundFrame frame))
            {
                try
                {
                    if (!Lifetime.IsCancellationRequested)
                        await WriteFrameAsync(frame.Bytes);
                }
                finally
                {
                    frame.Ticket?.Release();
                }
            }
        }
    }

    /// <summary>
    ///     This method writes one frame under the write deadline and aborts the session when the
    ///     write fails or misses it.
    /// </summary>
    private async Task WriteFrameAsync(byte[] frameBytes)
    {
        try
        {
            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
            deadline.CancelAfter(_idleTimeout);

            await _stream.WriteAsync(frameBytes, deadline.Token);
            await _stream.FlushAsync(deadline.Token);
        }
        catch (Exception exception)
        {
            if (!Lifetime.IsCancellationRequested)
                _logger.LogDebug(
                    exception,
                    "Closing the connection from {Endpoint}: a write failed or took longer than {Timeout}.",
                    _remoteAddress,
                    _idleTimeout
                );

            Abort();
        }
    }

    /// <summary>
    ///     This method queues the reply of one query, or discards it and counts it.
    /// </summary>
    private void QueueQueryResponse(QueryTicket ticket, ReadOnlySpan<byte> responseBytes)
    {
        // A ticket that is already released belongs to a connection that has ended.
        if (ticket.IsReleased)
            return;

        if (
            responseBytes.Length > MaxFrameLength
            || !_outbound.Writer.TryWrite(new OutboundFrame(BuildFrame(responseBytes), ticket))
        )
        {
            Interlocked.Increment(ref _droppedResponses);
            _logger.LogDebug(
                "Discarding a {Size}-octet reply for {Endpoint}: it does not fit a frame, the queue is full or the connection is closed.",
                responseBytes.Length,
                _remoteAddress
            );
            ticket.Release();
        }
    }

    /// <summary>
    ///     This method prefixes a message with its two-octet length.
    /// </summary>
    private static byte[] BuildFrame(ReadOnlySpan<byte> messageBytes)
    {
        if (messageBytes.Length > MaxFrameLength)
            throw new ArgumentException("The message is longer than a frame can carry.");

        byte[] frame = new byte[2 + messageBytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)messageBytes.Length);
        messageBytes.CopyTo(frame.AsSpan(2));
        return frame;
    }

    private QueryTicket CurrentTicket() =>
        _currentTicket
        ?? throw new InvalidOperationException(
            "Responder callbacks can only be created while handling a frame."
        );

    /// <summary>
    ///     This method fills a buffer completely, or reports a clean disconnect.
    /// </summary>
    /// <returns>
    ///     <see langword="true" /> once the buffer is full, or <see langword="false" /> if the
    ///     connection closed before the first octet of this read (between messages).
    /// </returns>
    /// <exception cref="IOException">The connection closed partway through the read.</exception>
    private async ValueTask<bool> ReadExactAsync(byte[] buffer, CancellationToken token)
    {
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = await _stream.ReadAsync(buffer.AsMemory(totalRead), token);

            if (read == 0)
            {
                if (totalRead == 0)
                    return false;

                throw new IOException("The connection closed partway through a DNS message.");
            }

            totalRead += read;
        }

        return true;
    }

    /// <summary>
    ///     This record holds a framed message waiting for the writer, with the slot to release
    ///     once it is written or discarded (<see langword="null" /> for frames outside the slot
    ///     budget).
    /// </summary>
    private sealed record OutboundFrame(byte[] Bytes, QueryTicket Ticket);

    /// <summary>
    ///     This class is the in-flight slot of one query. Releasing is idempotent, so a reply, a
    ///     drop notice and the teardown sweep can all release it without double counting.
    /// </summary>
    private sealed class QueryTicket(FramedDnsStreamSession owner)
    {
        private int _released;

        public bool IsReleased => Volatile.Read(ref _released) != 0;

        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            owner._tickets.TryRemove(this, out _);
            owner._slots.Release();
        }
    }
}
