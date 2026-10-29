using System.Net;
using System.Net.Sockets;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;

// Define our namespace.
namespace Ninlil.Benchmarks.Pipeline;

/// <summary>
///     This class is the upstream resolver of the pipeline benchmarks: a UDP socket on
///     <c>127.0.0.1</c> and an OS-assigned port that answers every query with one A record
///     (<see cref="AnswerAddress" />, TTL <see cref="AnswerTimeToLiveSeconds" />) for the queried
///     name, after an optional fixed delay. It counts the queries it receives, so a benchmark
///     can prove that a cache hit never reaches it. The seeded forwarder of
///     <see cref="PipelineDataSet" /> is pointed at <see cref="Port" /> in each host's setup.
///     No benchmark touches the internet.
/// </summary>
public sealed class FakeUpstreamDnsServer : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the TTL of the answers; it is long enough that a cached answer
    ///     stays fresh for a whole run.
    /// </summary>
    public const uint AnswerTimeToLiveSeconds = 300;

    /// <summary>
    ///     This constant contains the address every answer carries.
    /// </summary>
    public static readonly IPAddress AnswerAddress = IPAddress.Parse("192.0.2.77");

    private readonly Socket _socket;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _loop;
    private readonly TimeSpan _delay;
    private long _received;

    /// <summary>
    ///     This constructor binds the socket and starts the receive loop.
    /// </summary>
    /// <param name="delay">How long the server waits before it answers each query.</param>
    public FakeUpstreamDnsServer(TimeSpan delay = default)
    {
        _delay = delay;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Port = ((IPEndPoint)_socket.LocalEndPoint).Port;
        _loop = Task.Run(() => ReceiveLoopAsync(_stopping.Token));
    }

    /// <summary>
    ///     This property contains the UDP port the server listens on.
    /// </summary>
    public int Port { get; }

    /// <summary>
    ///     This property contains the number of queries received so far.
    /// </summary>
    public long ReceivedCount => Interlocked.Read(ref _received);

    private async Task ReceiveLoopAsync(CancellationToken stoppingToken)
    {
        byte[] buffer = new byte[4096];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);

        while (!stoppingToken.IsCancellationRequested)
        {
            SocketReceiveFromResult result;

            try
            {
                result = await _socket.ReceiveFromAsync(buffer, any, stoppingToken);
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException)
            {
                break;
            }
            catch (SocketException)
            {
                // An ICMP error for an earlier reply must not stop the server.
                continue;
            }

            Interlocked.Increment(ref _received);
            byte[] query = buffer[..result.ReceivedBytes];
            EndPoint sender = result.RemoteEndPoint;

            // Each answer runs on its own task so a delay does not serialize the queries.
            _ = Task.Run(() => AnswerAsync(query, sender, stoppingToken), stoppingToken);
        }
    }

    private async Task AnswerAsync(byte[] query, EndPoint sender, CancellationToken stoppingToken)
    {
        try
        {
            DnsMessage request = DnsMessageReader.Read(query);

            if (request.Questions.Count == 0)
                return;

            DnsMessage response = new()
            {
                Id = request.Id,
                IsResponse = true,
                Opcode = request.Opcode,
                RecursionDesired = request.RecursionDesired,
                RecursionAvailable = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { request.Questions[0] },
            };
            response.Answers.Add(
                new DnsResourceRecord(
                    request.Questions[0].Name,
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    AnswerTimeToLiveSeconds,
                    new ARecordDataCodec().Write(new ARecordData(AnswerAddress))
                )
            );

            if (_delay > TimeSpan.Zero)
                await Task.Delay(_delay, stoppingToken);

            await _socket.SendToAsync(DnsMessageWriter.Write(response), sender, stoppingToken);
        }
        catch (Exception exception)
            when (exception is OperationCanceledException or ObjectDisposedException)
        {
            // The server is stopping.
        }
        catch (Exception exception) when (exception is SocketException or FormatException)
        {
            // A malformed query or a vanished client gets no answer.
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _socket.Dispose();
        await _loop;
        _stopping.Dispose();
    }
}
