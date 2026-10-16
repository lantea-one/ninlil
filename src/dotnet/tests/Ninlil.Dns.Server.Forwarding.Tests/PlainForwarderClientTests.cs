using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Forwarding.Clients;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class tests <see cref="PlainForwarderClient" />'s message-ID and reply-matching rules
///     against a scripted fake upstream on a loopback port: real UDP and TCP sockets, OS-assigned
///     port, the message-ID source injected through the internal constructor.
/// </summary>
public class PlainForwarderClientTests
{
    /// <summary>
    ///     This class is a fake upstream that listens on UDP and TCP on the same loopback port.
    ///     For each UDP datagram or TCP message it records the decoded query, then sends back
    ///     the replies a script returns (several datagrams are allowed, to model a forger or a
    ///     stray reply ahead of the right one). A script that returns nothing leaves the client
    ///     waiting.
    /// </summary>
    private sealed class ScriptedUpstream : IDisposable
    {
        private readonly Socket udpSocket;
        private readonly TcpListener tcpListener;
        private readonly CancellationTokenSource cts = new();
        private readonly Func<DnsMessage, bool, DnsMessage[]> script;
        private readonly List<DnsMessage> udpQueries = [];
        private readonly List<DnsMessage> tcpQueries = [];

        /// <summary>
        ///     This constructor binds a TCP listener on an OS-assigned port, then binds UDP to
        ///     the same number (retrying with a new TCP port when that number is taken).
        /// </summary>
        /// <param name="script">
        ///     Called with each decoded query and <see langword="true" /> for UDP; returns the
        ///     replies to send.
        /// </param>
        public ScriptedUpstream(Func<DnsMessage, bool, DnsMessage[]> script)
        {
            this.script = script;

            for (int attempt = 0; ; attempt++)
            {
                TcpListener candidate = new(IPAddress.Loopback, 0);
                candidate.Start();
                int port = ((IPEndPoint)candidate.LocalEndpoint).Port;
                Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                try
                {
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
                }
                catch (SocketException) when (attempt < 20)
                {
                    socket.Dispose();
                    candidate.Stop();
                    continue;
                }

                tcpListener = candidate;
                udpSocket = socket;
                break;
            }

            _ = RunUdpAsync(cts.Token);
            _ = RunTcpAsync(cts.Token);
        }

        public int Port => ((IPEndPoint)udpSocket.LocalEndPoint).Port;

        /// <summary>
        ///     This property contains a snapshot of the queries received over UDP.
        /// </summary>
        public DnsMessage[] UdpQueries
        {
            get
            {
                lock (udpQueries)
                    return [.. udpQueries];
            }
        }

        /// <summary>
        ///     This property contains a snapshot of the queries received over TCP.
        /// </summary>
        public DnsMessage[] TcpQueries
        {
            get
            {
                lock (tcpQueries)
                    return [.. tcpQueries];
            }
        }

        private async Task RunUdpAsync(CancellationToken stoppingToken)
        {
            byte[] buffer = new byte[4096];

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    SocketReceiveFromResult received = await udpSocket.ReceiveFromAsync(
                        buffer,
                        new IPEndPoint(IPAddress.Any, 0),
                        stoppingToken
                    );
                    DnsMessage query = DnsMessageReader.Read(
                        buffer.AsSpan(0, received.ReceivedBytes)
                    );

                    lock (udpQueries)
                        udpQueries.Add(query);

                    foreach (DnsMessage reply in script(query, true))
                        await udpSocket.SendToAsync(
                            DnsMessageWriter.Write(reply),
                            received.RemoteEndPoint,
                            stoppingToken
                        );
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException)
            {
                // Expected when Dispose() stops the loop.
            }
        }

        private async Task RunTcpAsync(CancellationToken stoppingToken)
        {
            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    using TcpClient connection = await tcpListener.AcceptTcpClientAsync(
                        stoppingToken
                    );
                    NetworkStream stream = connection.GetStream();

                    byte[] prefix = new byte[2];
                    await stream.ReadExactlyAsync(prefix, stoppingToken);
                    byte[] body = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
                    await stream.ReadExactlyAsync(body, stoppingToken);
                    DnsMessage query = DnsMessageReader.Read(body);

                    lock (tcpQueries)
                        tcpQueries.Add(query);

                    foreach (DnsMessage reply in script(query, false))
                    {
                        byte[] bytes = DnsMessageWriter.Write(reply);
                        BinaryPrimitives.WriteUInt16BigEndian(prefix, (ushort)bytes.Length);
                        await stream.WriteAsync(prefix, stoppingToken);
                        await stream.WriteAsync(bytes, stoppingToken);
                    }
                }
            }
            catch (Exception ex)
                when (ex
                        is OperationCanceledException
                            or SocketException
                            or IOException
                            or ObjectDisposedException
                )
            {
                // Expected when Dispose() stops the loop.
            }
        }

        public void Dispose()
        {
            cts.Cancel();
            udpSocket.Close();
            tcpListener.Stop();
            cts.Dispose();
        }
    }

    private static DnsQuestion Question(string name) =>
        new(DnsName.Parse(name), DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet);

    private static DnsMessage Query(string name, ushort id = 0x1234)
    {
        DnsMessage message = new()
        {
            Id = id,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = true,
        };
        message.Questions.Add(Question(name));
        return message;
    }

    /// <summary>
    ///     This method builds a reply for a name and ID, optionally truncated.
    /// </summary>
    private static DnsMessage Reply(string name, ushort id, bool truncated = false)
    {
        DnsMessage reply = new()
        {
            Id = id,
            IsResponse = true,
            IsTruncated = truncated,
            RecursionDesired = true,
            RecursionAvailable = true,
        };
        reply.Questions.Add(Question(name));
        return reply;
    }

    private static UpstreamForwarderDefinition DefinitionFor(ScriptedUpstream upstream) =>
        new(
            UpstreamForwarderProtocolEnum.Plain,
            "127.0.0.1:" + upstream.Port,
            IPAddress.Loopback,
            upstream.Port
        );

    /// <summary>
    ///     This method returns an ID source that hands out the given IDs in order.
    /// </summary>
    private static Func<ushort> Ids(params ushort[] ids)
    {
        int next = -1;
        return () => ids[Math.Min(Interlocked.Increment(ref next), ids.Length - 1)];
    }

    [Fact]
    public async Task ForwardAsync_Always_SendsTheGeneratedIdAndRestoresTheClientsIdOnTheReply()
    {
        using ScriptedUpstream upstream = new((query, _) => [Reply("www.example.com", query.Id)]);
        PlainForwarderClient client = new(null, null, Ids(0xBEEF));
        DnsMessage query = Query("www.example.com", 0x1234);

        DnsMessage reply = await client.ForwardAsync(DefinitionFor(upstream), query);

        Assert.Equal(0xBEEF, Assert.Single(upstream.UdpQueries).Id);
        Assert.Equal(0x1234, query.Id);
        Assert.Equal(0x1234, reply.Id);
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyToAnotherQuestionFirst_KeepsWaitingForTheRightReply()
    {
        using ScriptedUpstream upstream = new(
            (query, _) => [Reply("evil.example", query.Id), Reply("www.example.com", query.Id)]
        );
        PlainForwarderClient client = new(null, null, Ids(0xBEEF));

        DnsMessage reply = await client.ForwardAsync(
            DefinitionFor(upstream),
            Query("www.example.com")
        );

        Assert.Equal("www.example.com", reply.Questions[0].Name.ToString().TrimEnd('.'));
    }

    [Fact]
    public async Task ForwardAsync_ForAReplyWithAnotherIdFirst_KeepsWaitingForTheRightReply()
    {
        using ScriptedUpstream upstream = new(
            (query, _) =>
                [
                    Reply("www.example.com", (ushort)(query.Id + 1)),
                    Reply("www.example.com", query.Id),
                ]
        );
        PlainForwarderClient client = new(null, null, Ids(0xBEEF));

        DnsMessage reply = await client.ForwardAsync(
            DefinitionFor(upstream),
            Query("www.example.com")
        );

        Assert.True(reply.IsResponse);
    }

    [Fact]
    public async Task ForwardAsync_ForARepliesThatNeverMatch_EndsWithTheCallersTimeout()
    {
        using ScriptedUpstream upstream = new((query, _) => [Reply("evil.example", query.Id)]);
        PlainForwarderClient client = new(null, null, Ids(0xBEEF));
        using CancellationTokenSource timeout = new(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ForwardAsync(DefinitionFor(upstream), Query("www.example.com"), timeout.Token)
        );
    }

    [Fact]
    public async Task ForwardAsync_ForATruncatedUdpReply_RetriesOverTcpWithAFreshId()
    {
        using ScriptedUpstream upstream = new(
            (query, isUdp) => [Reply("www.example.com", query.Id, truncated: isUdp)]
        );
        PlainForwarderClient client = new(null, null, Ids(0x1111, 0x2222));
        DnsMessage query = Query("www.example.com", 0x1234);

        DnsMessage reply = await client.ForwardAsync(DefinitionFor(upstream), query);

        Assert.Equal(0x1111, Assert.Single(upstream.UdpQueries).Id);
        Assert.Equal(0x2222, Assert.Single(upstream.TcpQueries).Id);
        Assert.False(reply.IsTruncated);
        Assert.Equal(0x1234, reply.Id);
    }

    [Fact]
    public async Task ForwardAsync_ForATcpReplyWithAnotherId_Throws()
    {
        using ScriptedUpstream upstream = new(
            (query, isUdp) =>
                [Reply("www.example.com", isUdp ? query.Id : (ushort)(query.Id + 1), isUdp)]
        );
        PlainForwarderClient client = new(null, null, Ids(0x1111, 0x2222));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ForwardAsync(DefinitionFor(upstream), Query("www.example.com"))
        );
    }

    [Fact]
    public async Task ForwardAsync_ForATcpReplyToAnotherQuestion_Throws()
    {
        using ScriptedUpstream upstream = new(
            (query, isUdp) => [Reply(isUdp ? "www.example.com" : "evil.example", query.Id, isUdp)]
        );
        PlainForwarderClient client = new(null, null, Ids(0x1111, 0x2222));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            client.ForwardAsync(DefinitionFor(upstream), Query("www.example.com"))
        );
    }
}
