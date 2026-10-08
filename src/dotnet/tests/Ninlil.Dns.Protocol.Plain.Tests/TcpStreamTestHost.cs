using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.Plain.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.Plain.Tests;

/// <summary>
///     This class starts a <see cref="TcpDnsListener" /> with a real
///     <see cref="DnsQueryProcessingPipeline" /> on a free loopback port, for the tests that
///     check how the listener behaves under pipelining, a stalled reader and a long zone
///     transfer. A query name decides what the fake resolver does: a name starting with
///     <c>slow</c> waits on <see cref="Gate" />, a name starting with <c>big</c> gets a reply of
///     about 56 000 octets, and anything else is answered at once. An optional fake
///     <see cref="IZoneTransferService" /> serves AXFR queries.
/// </summary>
internal sealed class TcpStreamTestHost : IAsyncDisposable
{
    private DnsQueryProcessingPipeline _pipeline;
    private TcpDnsListener _tcp;

    public int Port { get; private init; }

    /// <summary>
    ///     This property contains the gate the <c>slow</c> queries wait on; completing it lets
    ///     all of them answer.
    /// </summary>
    public TaskCompletionSource Gate { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    ///     This property contains how many <c>slow</c> queries are inside the resolver now.
    /// </summary>
    public int SlowInFlight => Volatile.Read(ref _slowInFlight);

    /// <summary>
    ///     This property contains the most <c>slow</c> queries the resolver ever held at once.
    /// </summary>
    public int MaxSlowInFlight => Volatile.Read(ref _maxSlowInFlight);

    private int _slowInFlight;
    private int _maxSlowInFlight;
    private readonly SemaphoreSlim _slowArrived = new(0);

    public static async Task<TcpStreamTestHost> StartAsync(
        TimeSpan idleTimeout,
        IZoneTransferService transferService = null
    )
    {
        TcpStreamTestHost host = new() { Port = LocalAddresses.GetFreePort() };

        host._pipeline = new DnsQueryProcessingPipeline(
            new DnsQueryDispatcher([new HostResolver(host)], new NoOpEventPublisher()),
            new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                new DnsQueryProcessingPipelineOptions()
            ),
            NullLogger<DnsQueryProcessingPipeline>.Instance
        );
        await host._pipeline.StartAsync(CancellationToken.None);

        ServiceCollection services = new();

        if (transferService is not null)
            services.AddSingleton(transferService);

        host._tcp = new TcpDnsListener(
            host._pipeline,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new StaticOptionsMonitor<PlainProtocolOptions>(
                new PlainProtocolOptions
                {
                    Port = host.Port,
                    ListenAddresses = "127.0.0.1",
                    TcpIdleTimeout = idleTimeout,
                }
            ),
            NullLogger<TcpDnsListener>.Instance
        );
        await host._tcp.StartAsync(CancellationToken.None);

        // BackgroundService runs ExecuteAsync (which binds) as a continuation after StartAsync
        // returns; wait until the listener reports it is accepting.
        while (!host._tcp.IsListening)
            await Task.Delay(10);

        return host;
    }

    /// <summary>
    ///     This method waits until a <c>slow</c> query reaches the resolver.
    /// </summary>
    public Task WaitForSlowQueryAsync() => _slowArrived.WaitAsync(TimeSpan.FromSeconds(30));

    public async ValueTask DisposeAsync()
    {
        Gate.TrySetResult();
        await _tcp.StopAsync(CancellationToken.None);
        await _pipeline.StopAsync(CancellationToken.None);
        await _pipeline.DisposeAsync();
    }

    /// <summary>
    ///     This method builds one length-prefixed query frame for a name and type.
    /// </summary>
    public static byte[] BuildFrame(
        ushort id,
        string name,
        DnsResourceRecordTypeEnum type = DnsResourceRecordTypeEnum.A
    )
    {
        byte[] message = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = id,
                Opcode = DnsOpcodeEnum.Query,
                RecursionDesired = true,
                Questions = { new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet) },
            }
        );
        byte[] frame = new byte[2 + message.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)message.Length);
        message.CopyTo(frame, 2);
        return frame;
    }

    /// <summary>
    ///     This method reads one framed message, or returns <see langword="null" /> when the
    ///     server closed the connection (an orderly end or a reset).
    /// </summary>
    public static async Task<DnsMessage> ReadMessageOrEndAsync(NetworkStream stream)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(30));
        byte[] prefix = new byte[2];

        try
        {
            await stream.ReadExactlyAsync(prefix, timeout.Token);
            byte[] message = new byte[BinaryPrimitives.ReadUInt16BigEndian(prefix)];
            await stream.ReadExactlyAsync(message, timeout.Token);
            return DnsMessageReader.Read(message, out _);
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException)
        {
            return null;
        }
    }

    private sealed class HostResolver(TcpStreamTestHost host) : IDnsQueryResolver
    {
        public async ValueTask<DnsQueryResolution> TryResolveAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            string name = context.Request.Questions[0].Name.ToString();
            DnsMessage response = new()
            {
                Id = context.Request.Id,
                IsResponse = true,
                Opcode = context.Request.Opcode,
                RecursionDesired = context.Request.RecursionDesired,
                ResponseCode = DnsResponseCodeEnum.NoError,
                Questions = { context.Request.Questions[0] },
            };

            if (name.StartsWith("slow", StringComparison.OrdinalIgnoreCase))
            {
                int now = Interlocked.Increment(ref host._slowInFlight);
                int max;

                do max = Volatile.Read(ref host._maxSlowInFlight);
                while (
                    now > max
                    && Interlocked.CompareExchange(ref host._maxSlowInFlight, now, max) != max
                );

                host._slowArrived.Release();

                try
                {
                    await host.Gate.Task.WaitAsync(stoppingToken);
                }
                finally
                {
                    Interlocked.Decrement(ref host._slowInFlight);
                }
            }
            else if (name.StartsWith("big", StringComparison.OrdinalIgnoreCase))
            {
                for (int index = 0; index < 3500; index++)
                    response.Answers.Add(
                        new DnsResourceRecord(
                            context.Request.Questions[0].Name,
                            DnsResourceRecordTypeEnum.A,
                            DnsClassEnum.Internet,
                            60,
                            [192, 0, 2, (byte)(index % 250)]
                        )
                    );
            }

            return DnsQueryResolution.Handled(response);
        }
    }

    private sealed class NoOpEventPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
