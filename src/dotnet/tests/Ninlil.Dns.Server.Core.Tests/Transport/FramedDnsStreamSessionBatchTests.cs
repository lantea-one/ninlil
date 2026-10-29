using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests.Transport;

/// <summary>
///     This class tests how <see cref="FramedDnsStreamSession" /> batches its writes. The server
///     side of a real loopback connection is wrapped in a stream that counts writes and flushes,
///     records each write's size, and can hold a write until the test lets it go. Holding the
///     first write of a group lets the test queue the other replies of the group behind it, so
///     what the writer finds in its queue does not depend on timing. A handler stands in for the
///     pipeline and parks each query's responder, as in <see cref="FramedDnsStreamSessionTests" />.
/// </summary>
public sealed class FramedDnsStreamSessionBatchTests
{
    private static readonly TimeSpan SafetyTimeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task FiftyRepliesQueuedInGroups_ArriveAsFiftyCorrectFramesInQueueOrder_WithFewerWrites()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));
        List<byte[]> expected = [];
        int nextId = 0;

        // Four groups of at most 16 (the in-flight limit). Within a group the replies are queued
        // in the reverse of arrival order, so the queue order differs from the query order.
        foreach (int groupSize in new[] { 16, 16, 16, 2 })
        {
            TaskCompletionSource release = harness.Stream.HoldNextWrite();
            List<ReceivedQuery> group = [];

            for (int index = 0; index < groupSize; index++)
                await harness.SendFrameAsync(nextId++);

            for (int index = 0; index < groupSize; index++)
                group.Add(await harness.ReceiveAsync());

            for (int index = group.Count - 1; index >= 0; index--)
            {
                // Different lengths, including a short and a long one, and distinct content.
                byte[] reply = Enumerable
                    .Repeat((byte)(expected.Count + 1), 1 + (expected.Count * 7) % 300)
                    .ToArray();
                expected.Add(reply);
                await group[index].Respond(reply);

                // The first reply is alone in the held write; the others queue behind it.
                if (index == group.Count - 1)
                    await harness.Stream.WaitForHeldWriteAsync();
            }

            release.SetResult();

            foreach (byte[] want in expected.Skip(expected.Count - groupSize))
                Assert.Equal(want, await harness.ReadFrameAsync());
        }

        Assert.Equal(50, expected.Count);
        Assert.True(
            harness.Stream.WriteSizes.Count < 50,
            $"{harness.Stream.WriteSizes.Count} writes for 50 frames"
        );

        // Each group is two writes: the held first frame, then everything queued behind it.
        Assert.Equal(8, harness.Stream.WriteSizes.Count);
        Assert.Equal(harness.Stream.WriteSizes.Count, harness.Stream.FlushCount);
        Assert.Equal(expected.Sum(reply => 2 + reply.Length), harness.Stream.WriteSizes.Sum());
    }

    [Fact]
    public async Task ABatch_NeverExceedsTheCap_ExceptForOneFrameThatIsLargerOnItsOwn()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));
        TaskCompletionSource release = harness.Stream.HoldNextWrite();

        for (int id = 0; id < 8; id++)
            await harness.SendFrameAsync(id);

        List<ReceivedQuery> group = [];

        for (int index = 0; index < 8; index++)
            group.Add(await harness.ReceiveAsync());

        // 8 frames of 20,002 octets: the first is written alone while it is held, and the other
        // seven are split at the cap, three frames (60,006 octets) at a time.
        await group[0].Respond(Enumerable.Repeat((byte)1, 20_000).ToArray());
        await harness.Stream.WaitForHeldWriteAsync();

        for (int index = 1; index < 8; index++)
            await group[index].Respond(Enumerable.Repeat((byte)(index + 1), 20_000).ToArray());

        release.SetResult();

        for (int index = 0; index < 8; index++)
            Assert.Equal(
                Enumerable.Repeat((byte)(index + 1), 20_000).ToArray(),
                await harness.ReadFrameAsync()
            );

        Assert.All(
            harness.Stream.WriteSizes,
            size => Assert.True(size <= FramedDnsStreamSession.MaxBatchOctets)
        );
        Assert.Equal([20_002, 60_006, 60_006, 20_002], harness.Stream.WriteSizes);
    }

    [Fact]
    public async Task AFrameLargerThanTheCapAlone_IsWrittenWhole()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromSeconds(30));
        await harness.SendFrameAsync(1);
        ReceivedQuery query = await harness.ReceiveAsync();

        byte[] reply = Enumerable.Repeat((byte)9, 65_535).ToArray();
        await query.Respond(reply);

        Assert.Equal(reply, await harness.ReadFrameAsync());
        Assert.Equal([65_537], harness.Stream.WriteSizes);
    }

    [Fact]
    public async Task ABatchQueuedBehindAStalledPeer_MissesTheDeadlineOnce_AndFreesEverySlot()
    {
        await using Harness harness = await Harness.StartAsync(TimeSpan.FromMilliseconds(300));
        TaskCompletionSource release = harness.Stream.HoldNextWrite();

        for (int id = 0; id < 16; id++)
            await harness.SendFrameAsync(id);

        List<ReceivedQuery> group = [];

        for (int index = 0; index < 16; index++)
            group.Add(await harness.ReceiveAsync());

        await group[0].Respond(new byte[3_000]);
        await harness.Stream.WaitForHeldWriteAsync();

        foreach (ReceivedQuery query in group.Skip(1))
            await query.Respond(new byte[3_000]);

        // After the held write the writer finds the other replies queued, takes them as one batch,
        // and the peer never takes that write.
        harness.Stream.StallWritesAfterTheHeldOne();
        release.SetResult();

        await harness.RunTask.WaitAsync(SafetyTimeout);

        Assert.Equal(0, harness.Session.InFlightQueries);
        Assert.True(harness.Session.Lifetime.IsCancellationRequested);

        // The held write carried the first reply alone; the other fifteen went out as one batch,
        // which is the write the peer never took.
        Assert.Equal([3_002, 15 * 3_002], harness.Stream.WriteSizes);
    }

    /// <summary>
    ///     This record is one message the harness handler received, with the responder the
    ///     session created for it.
    /// </summary>
    private sealed record ReceivedQuery(DnsResponseSenderAsync Responder)
    {
        public ValueTask Respond(byte[] responseBytes) => Responder(responseBytes, default);
    }

    /// <summary>
    ///     This stream forwards to the server side of a loopback connection and records every
    ///     write's size and every flush. A test can hold the next write until it releases it, and
    ///     can make every write after that one wait until its token is cancelled, which stands in
    ///     for a peer that stopped reading.
    /// </summary>
    private sealed class CountingStream(NetworkStream inner) : Stream
    {
        private readonly object _gate = new();
        private readonly List<int> _writeSizes = [];
        private TaskCompletionSource _hold;
        private TaskCompletionSource _holdStarted = new();
        private bool _stall;
        private int _flushCount;

        public IReadOnlyList<int> WriteSizes
        {
            get
            {
                lock (_gate)
                    return [.. _writeSizes];
            }
        }

        public int FlushCount => Volatile.Read(ref _flushCount);

        /// <summary>
        ///     This method makes the next write wait until the returned source is completed. A
        ///     test that needs that write to carry exactly the frame it queued first waits for
        ///     <see cref="WaitForHeldWriteAsync" /> before it queues more.
        /// </summary>
        public TaskCompletionSource HoldNextWrite()
        {
            TaskCompletionSource hold = new(TaskCreationOptions.RunContinuationsAsynchronously);

            lock (_gate)
            {
                _hold = hold;
                _holdStarted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
            }

            return hold;
        }

        /// <summary>
        ///     This method completes once the writer has entered the write that
        ///     <see cref="HoldNextWrite" /> holds.
        /// </summary>
        public Task WaitForHeldWriteAsync()
        {
            lock (_gate)
                return _holdStarted.Task.WaitAsync(SafetyTimeout);
        }

        /// <summary>
        ///     This method makes every write after the held one wait for its token.
        /// </summary>
        public void StallWritesAfterTheHeldOne()
        {
            lock (_gate)
                _stall = true;
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default
        )
        {
            TaskCompletionSource hold;
            TaskCompletionSource started;
            bool stall;

            lock (_gate)
            {
                _writeSizes.Add(buffer.Length);
                hold = _hold;
                started = _holdStarted;
                _hold = null;
                stall = _stall && hold is null;
            }

            if (hold is not null)
            {
                started.TrySetResult();
                await hold.Task.WaitAsync(cancellationToken);
            }

            if (stall)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            await inner.WriteAsync(buffer, cancellationToken);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _flushCount);
            await inner.FlushAsync(cancellationToken);
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default
        ) => inner.ReadAsync(buffer, cancellationToken);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }

    /// <summary>
    ///     This class owns a client, an accepted server socket wrapped in a
    ///     <see cref="CountingStream" /> and a running session between them.
    /// </summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly Channel<ReceivedQuery> _received =
            Channel.CreateUnbounded<ReceivedQuery>();
        private Socket _serverSocket;
        private NetworkStream _clientStream;

        public TcpClient Client { get; private set; }

        public CountingStream Stream { get; private set; }

        public FramedDnsStreamSession Session { get; private set; }

        public Task RunTask { get; private set; }

        public static async Task<Harness> StartAsync(TimeSpan idleTimeout)
        {
            Harness harness = new();
            using TcpListener listener = new(IPAddress.Loopback, 0);
            listener.Start();

            harness.Client = new TcpClient(AddressFamily.InterNetwork);
            Task<Socket> accept = listener.AcceptSocketAsync();
            await harness.Client.ConnectAsync(
                IPAddress.Loopback,
                ((IPEndPoint)listener.LocalEndpoint).Port
            );
            harness._serverSocket = await accept;
            harness._clientStream = harness.Client.GetStream();
            harness.Stream = new CountingStream(
                new NetworkStream(harness._serverSocket, ownsSocket: false)
            );
            harness.Session = new FramedDnsStreamSession(
                harness.Stream,
                IPAddress.Loopback,
                idleTimeout,
                NullLogger.Instance,
                CancellationToken.None
            );
            harness.RunTask = harness.RunAndCloseAsync();
            return harness;
        }

        /// <summary>
        ///     This method sends one eight-octet query frame whose first two octets are
        ///     <paramref name="id" />.
        /// </summary>
        public async Task SendFrameAsync(int id)
        {
            byte[] frame = new byte[10];
            BinaryPrimitives.WriteUInt16BigEndian(frame, 8);
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), (ushort)id);
            await _clientStream.WriteAsync(frame);
        }

        public async Task<ReceivedQuery> ReceiveAsync()
        {
            using CancellationTokenSource timeout = new(SafetyTimeout);
            return await _received.Reader.ReadAsync(timeout.Token);
        }

        /// <summary>
        ///     This method reads one length-prefixed frame from the server.
        /// </summary>
        public async Task<byte[]> ReadFrameAsync()
        {
            using CancellationTokenSource timeout = new(SafetyTimeout);
            byte[] prefix = new byte[2];
            await _clientStream.ReadExactlyAsync(prefix, timeout.Token);
            byte[] frame = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
            await _clientStream.ReadExactlyAsync(frame, timeout.Token);
            return frame;
        }

        private async Task RunAndCloseAsync()
        {
            try
            {
                await Session.RunAsync(
                    (message, session, _) =>
                    {
                        _received.Writer.TryWrite(new ReceivedQuery(session.CreateResponder()));
                        return Task.FromResult(FrameOutcomeEnum.Enqueued);
                    }
                );
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
