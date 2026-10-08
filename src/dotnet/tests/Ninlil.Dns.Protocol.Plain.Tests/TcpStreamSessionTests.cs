using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Transport;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class tests <see cref="TcpDnsListener" /> through its
///     <see cref="FramedDnsStreamSession" /> on real loopback sockets with the real pipeline: a
///     client that pipelines queries and never reads, a client whose replies stall the socket,
///     and an ordinary client on another connection that must keep being answered. A gate in
///     the fake resolver (see <see cref="TcpStreamTestHost" />) holds queries so the number in
///     flight can be counted without timing.
/// </summary>
public sealed class TcpStreamSessionTests
{
    private static readonly TimeSpan NegativeWindow = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task AClientThatPipelinesTwentyQueriesAndReadsNone_HasAtMostSixteenInFlight()
    {
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(
            TimeSpan.FromSeconds(30)
        );
        using TcpClient greedy = await ConnectAsync(host.Port);
        NetworkStream greedyStream = greedy.GetStream();

        for (ushort id = 0; id < 20; id++)
            await greedyStream.WriteAsync(TcpStreamTestHost.BuildFrame(id, "slow.example."));

        for (
            int arrived = 0;
            arrived < FramedDnsStreamSession.MaxInFlightQueriesPerConnection;
            arrived++
        )
            await host.WaitForSlowQueryAsync();

        // The rest wait in the socket: the connection stopped reading at its limit.
        await Task.Delay(NegativeWindow);
        Assert.Equal(16, host.SlowInFlight);
        Assert.Equal(16, host.MaxSlowInFlight);

        // Another connection is answered while the first is at its limit.
        using TcpClient other = await ConnectAsync(host.Port);
        NetworkStream otherStream = other.GetStream();
        await otherStream.WriteAsync(TcpStreamTestHost.BuildFrame(500, "fast.example."));
        DnsMessage otherAnswer = await TcpStreamTestHost.ReadMessageOrEndAsync(otherStream);
        Assert.Equal(500, otherAnswer.Id);

        // Releasing the gate answers all twenty, including the four that waited.
        host.Gate.SetResult();
        HashSet<ushort> answered = [];

        for (int count = 0; count < 20; count++)
            answered.Add((await TcpStreamTestHost.ReadMessageOrEndAsync(greedyStream)).Id);

        Assert.Equal(20, answered.Count);
    }

    [Fact]
    public async Task AClientThatStopsReading_IsClosedAtTheWriteDeadline_WhileOthersAreAnswered()
    {
        await using TcpStreamTestHost host = await TcpStreamTestHost.StartAsync(
            TimeSpan.FromMilliseconds(500)
        );

        using TcpClient stalled = new(AddressFamily.InterNetwork) { ReceiveBufferSize = 4096 };
        await stalled.ConnectAsync(IPAddress.Loopback, host.Port);
        NetworkStream stalledStream = stalled.GetStream();

        // About 56 000 octets per reply. The client keeps sending queries and reads nothing:
        // once the socket buffers fill, the server's write stalls, it stops reading, and the
        // client's own writes block. The loop ends when the server closes the connection at the
        // write deadline, and a write then fails. 64 MiB of queries is far more than the
        // buffers on both ends can hold.
        byte[] oneFrame = TcpStreamTestHost.BuildFrame(1, "big.example.");
        byte[] batch = new byte[oneFrame.Length * 2000];

        for (int index = 0; index < 2000; index++)
            oneFrame.CopyTo(batch, index * oneFrame.Length);

        Task sender = Task.Run(async () =>
        {
            try
            {
                for (int round = 0; round < 1000; round++)
                    await stalledStream.WriteAsync(batch);

                Assert.Fail("The server never closed the connection.");
            }
            catch (IOException)
            {
                // The server closed the connection.
            }
        });

        // Other clients are answered while the first one is stuck.
        using TcpClient other = await ConnectAsync(host.Port);
        NetworkStream otherStream = other.GetStream();
        await otherStream.WriteAsync(TcpStreamTestHost.BuildFrame(77, "fast.example."));
        Assert.Equal(77, (await TcpStreamTestHost.ReadMessageOrEndAsync(otherStream)).Id);

        // The sender only stops when the server closes the connection at the write deadline.
        await sender.WaitAsync(TimeSpan.FromSeconds(30));

        // The client finally reads: what the server had already written arrives, and then the
        // server has closed the connection (the stall hit the deadline) instead of waiting.
        byte[] buffer = new byte[64 * 1024];
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        long total = 0;

        try
        {
            int read;

            while ((read = await stalledStream.ReadAsync(buffer, timeout.Token)) > 0)
                total += read;
        }
        catch (IOException)
        {
            // A reset ends the stream just as well as an orderly close.
        }

        // The freed connection leaves the server able to answer new clients.
        using TcpClient later = await ConnectAsync(host.Port);
        NetworkStream laterStream = later.GetStream();
        await laterStream.WriteAsync(TcpStreamTestHost.BuildFrame(78, "fast.example."));
        Assert.Equal(78, (await TcpStreamTestHost.ReadMessageOrEndAsync(laterStream)).Id);
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        TcpClient client = new(AddressFamily.InterNetwork);
        await client.ConnectAsync(IPAddress.Loopback, port);
        return client;
    }
}
