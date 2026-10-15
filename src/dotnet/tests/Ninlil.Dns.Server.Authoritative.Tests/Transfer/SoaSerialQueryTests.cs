using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests <c>ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync</c>, the UDP SOA check
///     every transfer client runs before a pull. The primary is a loopback UDP socket on an
///     OS-assigned port, a second loopback socket plays a spoofer, and the TSIG service is the real
///     one from the shared <see cref="TsigTransferHost" />. A capturing logger records the level of
///     each message.
/// </summary>
public sealed class SoaSerialQueryTests(TsigTransferFixture fixture)
    : IClassFixture<TsigTransferFixture>,
        IDisposable
{
    private static readonly DnsName Zone = DnsName.Parse(TsigTransferHost.ZoneName);

    // The scope the TSIG service comes from; it lives as long as the test.
    private readonly IServiceScope _scope = fixture.Host.Services.CreateScope();

    private readonly CapturingLogger _logger = new();

    /// <summary>
    ///     This class is an <see cref="ILogger" /> that keeps every formatted message with its level,
    ///     so a test can assert that a rejection is a Warning and not a Debug message.
    /// </summary>
    private sealed class CapturingLogger : ILogger
    {
        /// <summary>The captured messages, guarded by a lock on the list itself.</summary>
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state) => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        )
        {
            lock (Entries)
                Entries.Add((logLevel, formatter(state, exception)));
        }

        /// <summary>This method returns the messages at one level.</summary>
        public List<string> At(LogLevel level)
        {
            lock (Entries)
                return Entries.Where(entry => entry.Level == level).Select(e => e.Message).ToList();
        }
    }

    /// <summary>
    ///     This class is a fake primary: one loopback UDP socket whose handler is given the query and
    ///     the sender and sends whatever datagrams the test wants.
    /// </summary>
    private sealed class FakeUdpPrimary : IDisposable
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(20));

        public FakeUdpPrimary(Func<UdpClient, DnsMessage, IPEndPoint, Task> handler)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    UdpReceiveResult received = await _socket.ReceiveAsync(_cts.Token);
                    DnsMessage query = DnsMessageReader.Read(received.Buffer, out _);
                    await handler(_socket, query, received.RemoteEndPoint);
                }
                catch (Exception exception)
                    when (exception is OperationCanceledException or ObjectDisposedException) { }
            });
        }

        /// <summary>The port the primary listens on.</summary>
        public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint).Port;

        public void Dispose()
        {
            _cts.Cancel();
            _socket.Dispose();
        }
    }

    private static DnsMessage SoaAnswer(DnsMessage query, uint serial, ushort? id = null)
    {
        DnsMessage reply = new()
        {
            Id = id ?? query.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            IsAuthoritativeAnswer = true,
            Questions = { query.Questions[0] },
        };
        reply.Answers.Add(
            new DnsResourceRecord(
                Zone,
                DnsResourceRecordTypeEnum.Soa,
                DnsClassEnum.Internet,
                3600,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
                )
            )
        );
        return reply;
    }

    private static Task SendAsync(UdpClient socket, DnsMessage message, IPEndPoint destination) =>
        socket.SendAsync(DnsMessageWriter.Write(message), destination).AsTask();

    private Task<uint?> QueryAsync(int port, string key, TimeSpan? timeout = null) =>
        ZoneTransferPullCodec.QuerySoaSerialOverUdpAsync(
            IPAddress.Loopback,
            Zone,
            key,
            port,
            timeout ?? TimeSpan.FromSeconds(5),
            _scope.ServiceProvider.GetRequiredService<ITsigService>(),
            CancellationToken.None,
            _logger
        );

    /// <inheritdoc />
    public void Dispose() => _scope.Dispose();

    [Fact]
    public async Task QuerySoaSerial_WithAKeyAndAnUnsignedAnswer_ReturnsNullAndLogsOneWarning()
    {
        string key = await fixture.Host.AddKeyAsync();
        using FakeUdpPrimary primary = new(
            (socket, query, sender) => SendAsync(socket, SoaAnswer(query, 42), sender)
        );

        uint? serial = await QueryAsync(primary.Port, key);

        Assert.Null(serial);
        string warning = Assert.Single(_logger.At(LogLevel.Warning));
        Assert.Contains(TsigTransferHost.ZoneName.TrimEnd('.'), warning);
        Assert.Contains("127.0.0.1", warning);
        Assert.Contains(key, warning);
    }

    [Fact]
    public async Task QuerySoaSerial_WithAnAnswerSignedByAnotherKey_ReturnsNullAndLogsTheTsigError()
    {
        string key = await fixture.Host.AddKeyAsync();
        string otherKey = await fixture.Host.AddKeyAsync();
        using FakeUdpPrimary primary = new(
            async (socket, query, sender) =>
            {
                DnsMessage signed = await fixture.Host.WithTsigAsync(tsig =>
                    tsig.SignResponseAsync(SoaAnswer(query, 42), otherKey, query.Id, query.Tsig.Mac)
                );
                await SendAsync(socket, signed, sender);
            }
        );

        uint? serial = await QueryAsync(primary.Port, key);

        Assert.Null(serial);
        string warning = Assert.Single(_logger.At(LogLevel.Warning));
        Assert.Contains("failed TSIG verification", warning);
        Assert.Contains(nameof(DnsResponseCodeEnum.BadKey), warning);
        Assert.Contains(key, warning);
    }

    [Fact]
    public async Task QuerySoaSerial_WithACorrectlySignedAnswer_ReturnsTheSerialAndLogsNoWarning()
    {
        string key = await fixture.Host.AddKeyAsync();
        using FakeUdpPrimary primary = new(
            async (socket, query, sender) =>
            {
                DnsMessage signed = await fixture.Host.WithTsigAsync(tsig =>
                    tsig.SignResponseAsync(SoaAnswer(query, 42), key, query.Id, query.Tsig.Mac)
                );
                await SendAsync(socket, signed, sender);
            }
        );

        Assert.Equal(42u, await QueryAsync(primary.Port, key));
        Assert.Empty(_logger.At(LogLevel.Warning));
    }

    [Fact]
    public async Task QuerySoaSerial_ARightIdReplyFromAnotherSocket_IsIgnoredInFavourOfThePrimary()
    {
        using UdpClient spoofer = new(new IPEndPoint(IPAddress.Loopback, 0));
        using FakeUdpPrimary primary = new(
            async (socket, query, sender) =>
            {
                // The spoofer's datagram, with the right ID, reaches the client first.
                await SendAsync(spoofer, SoaAnswer(query, 99), sender);
                await Task.Delay(100);
                await SendAsync(socket, SoaAnswer(query, 42), sender);
            }
        );

        Assert.Equal(42u, await QueryAsync(primary.Port, null));
    }

    [Fact]
    public async Task QuerySoaSerial_AReplyWithAWrongIdBeforeTheRightOne_KeepsWaiting()
    {
        using FakeUdpPrimary primary = new(
            async (socket, query, sender) =>
            {
                await SendAsync(socket, SoaAnswer(query, 99, (ushort)(query.Id + 1)), sender);
                await SendAsync(socket, SoaAnswer(query, 42), sender);
            }
        );

        Assert.Equal(42u, await QueryAsync(primary.Port, null));
    }

    [Fact]
    public async Task QuerySoaSerial_WithNoReply_ReturnsNullAfterTheTimeout()
    {
        using FakeUdpPrimary primary = new((socket, query, sender) => Task.CompletedTask);

        Assert.Null(await QueryAsync(primary.Port, null, TimeSpan.FromMilliseconds(300)));
    }
}
