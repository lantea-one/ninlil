using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;
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
///         <b>Batched writes.</b> The writer takes every frame already waiting in the queue, up to
///         <see cref="MaxBatchOctets" /> octets, builds each as its length prefix followed by its
///         message in one buffer, and writes the buffer with one write and one flush under one
///         deadline. A length and its message therefore always go to the transport in one write,
///         as RFC 7766 §8 recommends (over TLS, in one record), and replies that finish together
///         share a write instead of paying for one each. The writer never waits for more frames
///         to arrive, so a lone reply is written at once. Frames leave in queue order, and each
///         carries its own length, so a reply that finishes before an earlier query's reply is
///         framed exactly as if it were alone; RFC 7766 §7 lets a server send replies to
///         pipelined queries out of order. Every ticket of a batch is released once the batch has
///         been written or abandoned.
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
    ///     This constant contains the most octets, prefixes included, the writer puts into one
    ///     write. A single frame longer than this (a reply of up to 65 535 octets) is written
    ///     alone. The cap bounds the memory of a batch and keeps the one deadline a batch shares
    ///     meaningful for a slow reader.
    /// </summary>
    public const int MaxBatchOctets = 65536;

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
    ///     This field is the two-octet buffer the read loop fills with each frame's length. Only
    ///     the read loop touches it and it finishes with the value before it reads the message,
    ///     so one buffer serves the whole session.
    /// </summary>
    private readonly byte[] _readPrefix = new byte[2];

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
    ///     This method creates the token a zone transfer runs on: it is cancelled when the
    ///     session ends or after <paramref name="maxDuration" />, whichever comes first. The
    ///     read-idle timeout does not apply, because a transfer is long by nature; a secondary
    ///     that stops reading is cut by the write deadline instead. The caller disposes the
    ///     source.
    /// </summary>
    /// <param name="maxDuration">
    ///     The longest the transfer may take; callers pass <see cref="MaxTransferDuration" />.
    /// </param>
    public CancellationTokenSource CreateTransferTokenSource(TimeSpan maxDuration)
    {
        CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
        source.CancelAfter(maxDuration);
        return source;
    }

    /// <summary>
    ///     This method returns the delegate that sends a reply for the message the handler is
    ///     processing. It queues the frame and returns at once, so a pipeline slot never waits on
    ///     a socket. The slot of the message is released when the frame has been written, or when
    ///     it is discarded. A reply that is a whole array is queued without a copy, so the caller
    ///     must not change it afterwards; any other memory is copied.
    /// </summary>
    public DnsResponseSenderAsync CreateResponder()
    {
        QueryTicket ticket = CurrentTicket();

        return (responseBytes, _) =>
        {
            QueueQueryResponse(ticket, responseBytes);
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
            if (messageBytes.Length > MaxFrameLength)
                throw new ArgumentException("The message is longer than a frame can carry.");

            // Copied: the caller may reuse its buffer as soon as this call returns, while the
            // frame waits in the queue.
            await _outbound.Writer.WriteAsync(
                new OutboundFrame(messageBytes.ToArray(), null),
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
                    if (!await ReadExactAsync(_readPrefix, idleCts.Token))
                    {
                        ticket.Release();
                        return true;
                    }

                    // The message is a new array for every query: the pipeline keeps a reference
                    // to it for the life of the query (see DnsInboundMessage.RequestBytes), so it
                    // must never be a slice of a reused buffer.
                    message = new byte[BinaryPrimitives.ReadUInt16BigEndian(_readPrefix)];

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
    ///     This method waits until no query is in flight, by taking every slot, but for no longer
    ///     than the idle timeout. A reply is bounded by the write deadline once it exists, but a
    ///     query that hangs in the pipeline never produces one, and without this bound it would
    ///     hold the connection and its connection-limiter slot until shutdown. When the bound
    ///     passes, the session is aborted as a missed write deadline does: queued frames are
    ///     discarded and the remaining slots are released by the teardown.
    /// </summary>
    private async Task WaitForInFlightQueriesAsync()
    {
        using CancellationTokenSource drainCts = CancellationTokenSource.CreateLinkedTokenSource(
            Lifetime
        );
        drainCts.CancelAfter(_idleTimeout);

        try
        {
            for (int taken = 0; taken < MaxInFlightQueriesPerConnection; taken++)
                await _slots.WaitAsync(drainCts.Token);
        }
        catch (OperationCanceledException) when (!Lifetime.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Closing the connection from {Endpoint}: {Count} queries were still unanswered {Timeout} after it went idle.",
                _remoteAddress,
                InFlightQueries,
                _idleTimeout
            );
            Abort();
        }
    }

    /// <summary>
    ///     This method writes the outbound queue in batches: each pass takes the first waiting
    ///     frame and every frame behind it that fits <see cref="MaxBatchOctets" />, writes them
    ///     together within the idle timeout and releases their tickets. A failed or late write
    ///     aborts the session; the loop then discards what is left, still releasing every ticket.
    ///     It never throws.
    /// </summary>
    private async Task WriteLoopAsync()
    {
        List<OutboundFrame> batch = [];

        while (await _outbound.Reader.WaitToReadAsync())
        {
            while (_outbound.Reader.TryRead(out OutboundFrame first))
            {
                batch.Clear();
                batch.Add(first);
                int batchOctets = FrameOctets(first);

                // This loop is the queue's only reader, so the frame that TryPeek shows is the
                // one TryRead returns.
                while (
                    _outbound.Reader.TryPeek(out OutboundFrame next)
                    && batchOctets + FrameOctets(next) <= MaxBatchOctets
                    && _outbound.Reader.TryRead(out next)
                )
                {
                    batch.Add(next);
                    batchOctets += FrameOctets(next);
                }

                try
                {
                    if (!Lifetime.IsCancellationRequested)
                        await WriteBatchAsync(batch, batchOctets);
                }
                finally
                {
                    // Once per frame: a ticket's release is idempotent, and the teardown sweep
                    // may release the same ticket again.
                    foreach (OutboundFrame frame in batch)
                        frame.Ticket?.Release();
                }
            }
        }
    }

    /// <summary>
    ///     This method gives the octets a frame takes on the wire: the two-octet length prefix and
    ///     the message.
    /// </summary>
    private static int FrameOctets(OutboundFrame frame) => 2 + frame.Message.Length;

    /// <summary>
    ///     This method writes a batch of frames as one write and one flush under one write
    ///     deadline, and aborts the session when the write fails or misses the deadline. The
    ///     frames are copied into one rented buffer, each as its length prefix and its message, so
    ///     a prefix and its message always share a write.
    /// </summary>
    private async Task WriteBatchAsync(List<OutboundFrame> batch, int batchOctets)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(batchOctets);

        try
        {
            int offset = 0;

            foreach (OutboundFrame frame in batch)
            {
                BinaryPrimitives.WriteUInt16BigEndian(
                    buffer.AsSpan(offset),
                    (ushort)frame.Message.Length
                );
                frame.Message.Span.CopyTo(buffer.AsSpan(offset + 2));
                offset += FrameOctets(frame);
            }

            using CancellationTokenSource deadline =
                CancellationTokenSource.CreateLinkedTokenSource(Lifetime);
            deadline.CancelAfter(_idleTimeout);

            await _stream.WriteAsync(buffer.AsMemory(0, batchOctets), deadline.Token);
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
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     This method queues the reply of one query, or discards it and counts it.
    /// </summary>
    private void QueueQueryResponse(QueryTicket ticket, ReadOnlyMemory<byte> responseBytes)
    {
        // A ticket that is already released belongs to a connection that has ended.
        if (ticket.IsReleased)
            return;

        if (
            responseBytes.Length > MaxFrameLength
            || !_outbound.Writer.TryWrite(
                new OutboundFrame(WholeArrayOrCopy(responseBytes), ticket)
            )
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
    ///     This method returns the array behind an encoded reply when the memory covers all of it,
    ///     and a copy otherwise. The pipeline hands each reply to the responder once and never
    ///     touches it again, so the queue can keep the array until the writer has copied it into
    ///     its batch buffer. A slice or memory without an array is copied, because the owner of
    ///     that memory may reuse it.
    /// </summary>
    private static ReadOnlyMemory<byte> WholeArrayOrCopy(ReadOnlyMemory<byte> responseBytes) =>
        MemoryMarshal.TryGetArray(responseBytes, out ArraySegment<byte> segment)
        && segment.Offset == 0
        && segment.Count == segment.Array.Length
            ? responseBytes
            : responseBytes.ToArray();

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
    ///     This record holds a message waiting for the writer, which adds its length prefix, with
    ///     the slot to release once it is written or discarded (<see langword="null" /> for frames
    ///     outside the slot budget).
    /// </summary>
    private sealed record OutboundFrame(ReadOnlyMemory<byte> Message, QueryTicket Ticket);

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
