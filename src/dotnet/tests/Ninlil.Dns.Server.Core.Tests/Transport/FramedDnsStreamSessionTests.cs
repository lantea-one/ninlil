using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class tests <see cref="FramedDnsStreamSession" /> over real loopback sockets. A
///     handler stands in for the pipeline: it parks each received message's responder and drop
///     notifier, and the test completes them when it chooses, so the order of events is set by
///     the test and not by timing. Receive and send buffers are shrunk where a test needs a peer
///     that stalls the writer. A short idle timeout doubles as the write deadline, and the few
///     negative checks (nothing happens) wait a fixed short window.
/// </summary>
public sealed class FramedDnsStreamSessionTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NegativeWindow = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task APeerThatNeverReads_CannotHoldMoreThanSixteenQueriesInFlight()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        for (int id = 0; id < 20; id++)
            await harness.SendFrameAsync(id, 8);

        for (int count = 0; count < FramedDnsStreamSession.MaxInFlightQueriesPerConnection; count++)
            await harness.ReceiveAsync();

        Assert.Equal(16, harness.Session.InFlightQueries);

        // The other four frames stay unread while all slots are in use.
        Assert.False(harness.TryReceiveNow(out _));
        await Task.Delay(NegativeWindow);
        Assert.False(harness.TryReceiveNow(out _));
        Assert.Equal(16, harness.Session.InFlightQueries);
    }

    [Fact]
    public async Task ADroppedQuery_ReleasesItsSlotAndTheNextFrameIsRead()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        for (int id = 0; id < 17; id++)
            await harness.SendFrameAsync(id, 8);

        List<ReceivedQuery> received = [];

        for (int count = 0; count < 16; count++)
            received.Add(await harness.ReceiveAsync());

        received[3].Drop(DnsQueryDropReasonEnum.RateLimited);

        ReceivedQuery seventeenth = await harness.ReceiveAsync();
        Assert.Equal(16, BinaryPrimitives.ReadUInt16BigEndian(seventeenth.Message));
        Assert.Equal(16, harness.Session.InFlightQueries);
    }

    [Fact]
    public async Task APeerThatStopsReading_HitsTheWriteDeadline_AndFreesEverySlot()
    {
        await using Harness harness = await Harness.StartAsync(
            TimeSpan.FromMilliseconds(300),
            shrinkBuffers: true
        );

        for (int id = 0; id < 20; id++)
            await harness.SendFrameAsync(id, 8);

        List<ReceivedQuery> received = [];

        for (int count = 0; count < 16; count++)
            received.Add(await harness.ReceiveAsync());

        // Each responder only queues, however much the peer is behind.
        byte[] large = new byte[60_000];

        foreach (ReceivedQuery query in received)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            await query.Respond(large);
            Assert.True(stopwatch.Elapsed < TimeSpan.FromMilliseconds(100));
        }

        // Nothing reads the reply, so the writer misses its deadline and ends the session.
        await harness.RunTask.WaitAsync(SafetyTimeout);

        Assert.Equal(0, harness.Session.InFlightQueries);
        Assert.True(harness.Session.Lifetime.IsCancellationRequested);
    }

    [Fact]
    public async Task AStalledConnection_DoesNotStopAnotherConnectionFromBeingAnswered()
    {
        await using Harness stalled = await Harness.StartAsync(
            TimeSpan.FromSeconds(30),
            shrinkBuffers: true
        );
        await using Harness healthy = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        for (int id = 0; id < 16; id++)
            await stalled.SendFrameAsync(id, 8);

        for (int count = 0; count < 16; count++)
        {
            ReceivedQuery query = await stalled.ReceiveAsync();
            await query.Respond(new byte[60_000]);
        }

        await healthy.SendFrameAsync(7, 8);
        ReceivedQuery other = await healthy.ReceiveAsync();
        await other.Respond([1, 2, 3]);

        Assert.Equal([1, 2, 3], await healthy.ReadFrameAsync());
    }

    [Fact]
    public async Task RepliesFinishedOutOfOrder_AreEachFramedCorrectly()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        for (int id = 0; id < 3; id++)
            await harness.SendFrameAsync(id, 8);

        ReceivedQuery first = await harness.ReceiveAsync();
        ReceivedQuery second = await harness.ReceiveAsync();
        ReceivedQuery third = await harness.ReceiveAsync();

        await third.Respond(Enumerable.Repeat((byte)3, 300).ToArray());
        await first.Respond(Enumerable.Repeat((byte)1, 5).ToArray());
        await second.Respond(Enumerable.Repeat((byte)2, 70_00).ToArray());

        Assert.Equal(Enumerable.Repeat((byte)3, 300).ToArray(), await harness.ReadFrameAsync());
        Assert.Equal(Enumerable.Repeat((byte)1, 5).ToArray(), await harness.ReadFrameAsync());
        Assert.Equal(Enumerable.Repeat((byte)2, 70_00).ToArray(), await harness.ReadFrameAsync());
        Assert.Equal(0, harness.Session.InFlightQueries);
    }

    [Fact]
    public async Task AnIdleConnectionWithAQueryInFlight_StillDeliversTheAnswer_ThenCloses()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromMilliseconds(200));

        await harness.SendFrameAsync(1, 8);
        ReceivedQuery query = await harness.ReceiveAsync();

        // Several idle timeouts pass; the outstanding query keeps the connection open.
        await Task.Delay(TimeSpan.FromMilliseconds(800));
        Assert.False(harness.RunTask.IsCompleted);

        await query.Respond([9, 9]);
        Assert.Equal([9, 9], await harness.ReadFrameAsync());
        await harness.RunTask.WaitAsync(SafetyTimeout);

        // The server closed the connection: the client reads the end of the stream.
        Assert.Null(await harness.ReadFrameOrEndAsync());
    }

    [Fact]
    public async Task AClientThatClosesItsSide_StillGetsTheOutstandingAnswer()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        await harness.SendFrameAsync(1, 8);
        ReceivedQuery query = await harness.ReceiveAsync();
        harness.Client.Client.Shutdown(SocketShutdown.Send);

        await query.Respond([5]);

        Assert.Equal([5], await harness.ReadFrameAsync());
        await harness.RunTask.WaitAsync(SafetyTimeout);
    }

    [Fact]
    public async Task AbortWithQueriesInFlight_ReleasesEverySlot_AndLateRepliesAreIgnoredQuietly()
    {
        List<Exception> unobserved = [];
        EventHandler<UnobservedTaskExceptionEventArgs> hook = (_, args) =>
        {
            // The event is process-wide; only exceptions that came through the session count.
            if (args.Exception.ToString().Contains(nameof(FramedDnsStreamSession)))
                lock (unobserved)
                    unobserved.Add(args.Exception);
        };
        TaskScheduler.UnobservedTaskException += hook;

        try
        {
            Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));
            List<ReceivedQuery> received = [];

            for (int id = 0; id < 5; id++)
                await harness.SendFrameAsync(id, 8);

            for (int count = 0; count < 5; count++)
                received.Add(await harness.ReceiveAsync());

            await received[0].Respond([1]);
            Assert.Equal(5, harness.Session.InFlightQueries);

            harness.Session.Abort();
            await harness.RunTask.WaitAsync(SafetyTimeout);

            Assert.Equal(0, harness.Session.InFlightQueries);

            // The pipeline finishes after the connection is gone.
            await received[1].Respond([2]);
            received[2].Drop(DnsQueryDropReasonEnum.Failed);
            Assert.Equal(0, harness.Session.DroppedResponseCount);

            await harness.DisposeAsync();

            for (int round = 0; round < 3; round++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            lock (unobserved)
                Assert.Empty(unobserved);
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= hook;
        }
    }

    [Fact]
    public async Task HostShutdown_EndsTheSession_AndReleasesEverySlot()
    {
        using CancellationTokenSource stopping = new();
        await using Harness harness = await Harness.StartAsync(
            TimeSpan.FromSeconds(30),
            stopping: stopping.Token
        );

        for (int id = 0; id < 3; id++)
            await harness.SendFrameAsync(id, 8);

        for (int count = 0; count < 3; count++)
            await harness.ReceiveAsync();

        await stopping.CancelAsync();
        await harness.RunTask.WaitAsync(SafetyTimeout);

        Assert.Equal(0, harness.Session.InFlightQueries);
    }

    [Fact]
    public async Task AReplyThatDoesNotFitAFrame_IsDiscardedAndCounted()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        await harness.SendFrameAsync(1, 8);
        ReceivedQuery query = await harness.ReceiveAsync();

        await query.Respond(new byte[70_000]);

        Assert.Equal(1, harness.Session.DroppedResponseCount);
        Assert.Equal(0, harness.Session.InFlightQueries);
    }

    [Fact]
    public async Task SendAsync_WritesFramesInOrder_AndCloseAfterDrainFlushesThemBeforeClosing()
    {
        await using Harness harness = await Harness.StartAsync(
            TimeSpan.FromSeconds(30),
            handler: async (message, session, token) =>
            {
                for (byte part = 1; part <= 3; part++)
                    await session.SendAsync(new byte[] { part, part }, token);

                return FrameOutcomeEnum.CloseAfterDrain;
            }
        );

        await harness.SendFrameAsync(1, 8);

        Assert.Equal([1, 1], await harness.ReadFrameAsync());
        Assert.Equal([2, 2], await harness.ReadFrameAsync());
        Assert.Equal([3, 3], await harness.ReadFrameAsync());
        Assert.Null(await harness.ReadFrameOrEndAsync());
        await harness.RunTask.WaitAsync(SafetyTimeout);
    }

    [Fact]
    public async Task TheTransferToken_IsCancelledAtItsLimit_AndByAnAbort()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));

        using CancellationTokenSource limited = harness.Session.CreateTransferTokenSource(
            TimeSpan.FromMilliseconds(200)
        );

        // The two-hour limit is far from reached after the short one has passed.
        using CancellationTokenSource transfer = harness.Session.CreateTransferTokenSource(
            FramedDnsStreamSession.MaxTransferDuration
        );
        Assert.Equal(TimeSpan.FromHours(2), FramedDnsStreamSession.MaxTransferDuration);

        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.True(limited.IsCancellationRequested);
        Assert.False(transfer.IsCancellationRequested);

        harness.Session.Abort();
        Assert.True(transfer.IsCancellationRequested);
    }

    /// <summary>
    ///     This record is one message the harness handler received, with the callbacks the
    ///     session created for it.
    /// </summary>
    private sealed record ReceivedQuery(
        byte[] Message,
        DnsResponseSenderAsync Responder,
        DnsQueryDropNotifier Drop
    )
    {
        public ValueTask Respond(byte[] responseBytes) => Responder(responseBytes, default);
    }

    /// <summary>
    ///     This class owns a client, an accepted server socket and a running session between
    ///     them. Its default handler parks each message as a <see cref="ReceivedQuery" />.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Channel<ReceivedQuery> _received =
            Channel.CreateUnbounded<ReceivedQuery>();
        private Socket _serverSocket;
        private NetworkStream _clientStream;

        public TcpClient Client { get; private set; }

        public FramedDnsStreamSession Session { get; private set; }

        public Task RunTask { get; private set; }

        public static async Task<Harness> StartAsync(
            TimeSpan idleTimeout,
            bool shrinkBuffers = false,
            CancellationToken stopping = default,
            Func<
                byte[],
                FramedDnsStreamSession,
                CancellationToken,
                Task<FrameOutcomeEnum>
            > handler = null
        )
        {
            Harness harness = new();
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();

            harness.Client = new TcpClient(AddressFamily.InterNetwork);

            if (shrinkBuffers)
                harness.Client.ReceiveBufferSize = 4096;

            Task<Socket> accept = listener.AcceptSocketAsync();
            await harness.Client.ConnectAsync(
                IPAddress.Loopback,
                ((IPEndPoint)listener.LocalEndpoint).Port
            );
            harness._serverSocket = await accept;
            harness._clientStream = harness.Client.GetStream();

            if (shrinkBuffers)
                harness._serverSocket.SendBufferSize = 4096;

            harness.Session = new FramedDnsStreamSession(
                new NetworkStream(harness._serverSocket, ownsSocket: false),
                IPAddress.Loopback,
                idleTimeout,
                NullLogger.Instance,
                stopping
            );

            harness.RunTask = harness.RunAndCloseAsync(
                handler
                    ?? (
                        (message, session, _) =>
                        {
                            harness._received.Writer.TryWrite(
                                new ReceivedQuery(
                                    message,
                                    session.CreateResponder(),
                                    session.CreateDropNotifier()
                                )
                            );
                            return Task.FromResult(FrameOutcomeEnum.Enqueued);
                        }
                    )
            );

            return harness;
        }

        /// <summary>
        ///     This method sends one frame whose first two octets are <paramref name="id" />.
        /// </summary>
        public async Task SendFrameAsync(int id, int length)
        {
            byte[] frame = new byte[2 + length];
            BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)length);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)id);
            await _clientStream.WriteAsync(frame);
        }

        public async Task<ReceivedQuery> ReceiveAsync()
        {
            using CancellationTokenSource timeout = new(SafetyTimeout);
            return await _received.Reader.ReadAsync(timeout.Token);
        }

        public bool TryReceiveNow(out ReceivedQuery query) => _received.Reader.TryRead(out query);

        public async Task<byte[]> ReadFrameAsync() =>
            await ReadFrameOrEndAsync() ?? throw new EndOfStreamException();

        /// <summary>
        ///     This method reads one frame, or returns <see langword="null" /> when the server
        ///     closed the connection.
        /// </summary>
        public async Task<byte[]> ReadFrameOrEndAsync()
        {
            using CancellationTokenSource timeout = new(SafetyTimeout);
            NetworkStream stream = _clientStream;
            byte[] prefix = new byte[2];

            try
            {
                await stream.ReadExactlyAsync(prefix, timeout.Token);
                byte[] frame = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                await stream.ReadExactlyAsync(frame, timeout.Token);
                return frame;
            }
            catch (EndOfStreamException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        ///     This method runs the session and then closes the server socket, as the listeners
        ///     do, so the client can see the connection end.
        /// </summary>
        private async Task RunAndCloseAsync(
            Func<byte[], FramedDnsStreamSession, CancellationToken, Task<FrameOutcomeEnum>> handler
        )
        {
            try
            {
                await Session.RunAsync(handler);
            }
            finally
            {
                _serverSocket.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Session.Abort();
            await RunTask.WaitAsync(SafetyTimeout);
            Client.Dispose();
            _serverSocket.Dispose();
        }
    }
}
