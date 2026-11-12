using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsOverTls.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.RateLimiting;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsOverTls.Tests;

/// <summary>
///     This class tests that the connection of a zone transfer over <see cref="DnsOverTlsListener" />
///     ends in an orderly close even when the client sent one more message that the server never
///     read (the counterpart of <c>TcpZoneTransferCloseTests</c> in the plain DNS tests). A close
///     with unread data is a reset, and on Windows a reset makes the client discard the last
///     transfer message it had received but not yet read. The test uses a real listener, a TLS
///     client on a loopback port bound with port 0, and a fake <see cref="IZoneTransferService" />
///     that holds its last message until the test has put the extra message on the server's
///     socket.
/// </summary>
public sealed class DnsOverTlsZoneTransferCloseTests
{
    [Fact]
    public async Task AnAxfrClosedWithAnUnreadClientMessage_StillDeliversItsLastMessage()
    {
        GatedTransferService service = new();
        await using CloseHost host = await CloseHost.StartAsync(service);

        // A reset can be lost in the race with the client's read, so repeat the scenario.
        for (int round = 0; round < 20; round++)
        {
            service.ReleaseLastMessage = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );

            using SslStream stream = await host.ConnectAsync();
            await WriteQueryAsync(stream, 9, "zone.example.", DnsResourceRecordTypeEnum.Axfr);

            Assert.Equal(1, (await ReadMessageOrEndAsync(stream)).Id);

            // The session is inside the transfer and does not read; this frame stays unread in
            // the server's receive queue until the connection closes.
            await WriteQueryAsync(stream, 10, "other.example.", DnsResourceRecordTypeEnum.A);
            await Task.Delay(100);
            service.ReleaseLastMessage.SetResult();

            DnsMessage last = await ReadMessageOrEndAsync(stream);
            Assert.NotNull(last);
            Assert.Equal(2, last.Id);
            Assert.Null(await ReadMessageOrEndAsync(stream));
        }
    }

    /// <summary>
    ///     This method writes one length-prefixed query (RFC 7858 §3.3 uses the framing of RFC 1035
    ///     §4.2.2) to the stream.
    /// </summary>
    private static async Task WriteQueryAsync(
        Stream stream,
        ushort id,
        string name,
        DnsResourceRecordTypeEnum type
    )
    {
        byte[] message = DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = id,
                Opcode = DnsOpcodeEnum.Query,
                Questions = { new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet) },
            }
        );
        byte[] frame = new byte[2 + message.Length];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)message.Length);
        message.CopyTo(frame, 2);
        await stream.WriteAsync(frame);
    }

    /// <summary>
    ///     This method reads one length-prefixed message, or returns <see langword="null" /> when
    ///     the server ended the connection (an orderly close or a reset).
    /// </summary>
    private static async Task<DnsMessage> ReadMessageOrEndAsync(Stream stream)
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

    /// <summary>
    ///     This class starts a <see cref="DnsOverTlsListener" /> on loopback port 0 with the given
    ///     transfer service and a pipeline the listener needs but transfers do not use.
    /// </summary>
    private sealed class CloseHost : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;
        private DnsOverTlsListener _listener;

        /// <summary>
        ///     This method starts the host and waits until the listener is bound.
        /// </summary>
        public static async Task<CloseHost> StartAsync(IZoneTransferService service)
        {
            CloseHost host = new();
            host._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher([], new SilentPublisher()),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await host._pipeline.StartAsync(CancellationToken.None);

            host._listener = new DnsOverTlsListener(
                host._pipeline,
                new ServiceCollection()
                    .AddSingleton(service)
                    .BuildServiceProvider()
                    .GetRequiredService<IServiceScopeFactory>(),
                new StaticOptionsMonitor<DnsOverTlsOptions>(
                    new DnsOverTlsOptions
                    {
                        Enable = true,
                        Port = 0,
                        ListenAddresses = "127.0.0.1",
                        IdleTimeout = TimeSpan.FromSeconds(30),
                    }
                ),
                NullLogger<DnsOverTlsListener>.Instance,
                new AllowAllLimiter(),
                new SilentPublisher()
            );
            await host._listener.StartAsync(CancellationToken.None);

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));

            while (!host._listener.IsListening)
                await Task.Delay(10, timeout.Token);

            return host;
        }

        /// <summary>
        ///     This method opens a TLS connection to the listener, accepting its self-signed
        ///     certificate.
        /// </summary>
        public async Task<SslStream> ConnectAsync()
        {
            TcpClient client = new(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, _listener.BoundPort);
            SslStream stream = new(
                client.GetStream(),
                leaveInnerStreamOpen: false,
                (_, _, _, _) => true
            );
            await stream.AuthenticateAsClientAsync("localhost");
            return stream;
        }

        public async ValueTask DisposeAsync()
        {
            await _listener.StopAsync(CancellationToken.None);
            await _pipeline.StopAsync(CancellationToken.None);
            await _pipeline.DisposeAsync();
        }
    }

    /// <summary>
    ///     This limiter admits every query.
    /// </summary>
    private sealed class AllowAllLimiter : IDnsRateLimiter
    {
        public ValueTask<bool> TryAcquireAsync(
            IPAddress remoteAddress,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(true);

        public ValueTask<DnsSubnetRateLimitDecisionEnum> CheckSubnetLimitAsync(
            IPAddress remoteAddress,
            bool isUdp,
            CancellationToken stoppingToken = default
        ) => ValueTask.FromResult(DnsSubnetRateLimitDecisionEnum.Allow);
    }

    /// <summary>
    ///     This service yields two messages (ids 1 and 2) and holds the second until the test
    ///     sets <see cref="ReleaseLastMessage" />.
    /// </summary>
    private sealed class GatedTransferService : IZoneTransferService
    {
        public TaskCompletionSource ReleaseLastMessage { get; set; }

        public async IAsyncEnumerable<DnsMessage> CreateAxfrResponseAsync(
            DnsQueryContext context,
            [EnumeratorCancellation] CancellationToken stoppingToken = default
        )
        {
            yield return Message(1);
            await ReleaseLastMessage.Task.WaitAsync(stoppingToken);
            yield return Message(2);
        }

        public IAsyncEnumerable<DnsMessage> CreateIxfrResponseAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        ) => CreateAxfrResponseAsync(context, stoppingToken);

        private static DnsMessage Message(ushort id) =>
            new()
            {
                Id = id,
                IsResponse = true,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
    }

    /// <summary>This publisher discards every event.</summary>
    private sealed class SilentPublisher : IDnsServerEventPublisher
    {
        public void Publish(IDnsServerEvent domainEvent) { }
    }
}
