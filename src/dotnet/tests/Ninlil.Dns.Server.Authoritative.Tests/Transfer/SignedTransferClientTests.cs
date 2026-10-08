using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Tests.Fakes;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class tests the TSIG verification of the outbound clients: the AXFR pull of
///     <see cref="TcpZoneTransferClient" />, its UDP SOA check and
///     <see cref="StubZoneNsPuller" />. The client side is the real code with the real TSIG
///     service; the primary is a loopback fake (TCP and UDP on one port) that answers either with
///     the real <see cref="ZoneTransferService" /> or with a hand-built reply that the test signs
///     (or deliberately does not sign, or signs wrongly).
/// </summary>
public class SignedTransferClientTests(TsigTransferFixture fixture)
    : IClassFixture<TsigTransferFixture>,
        IDisposable
{
    // The scope the clients' ITsigService comes from; it lives as long as the test.
    private readonly IServiceScope _scope = fixture.Host.Services.CreateScope();

    private static readonly DnsName Zone = DnsName.Parse(TsigTransferHost.ZoneName);

    private readonly TsigTransferHost _host = fixture.Host;

    /// <summary>
    ///     This class is a fake primary: one TCP listener and one UDP socket on the same port
    ///     number (the clients use one configured port for both). Each connection or datagram is
    ///     handed to a test-supplied handler.
    /// </summary>
    private sealed class FakePrimary : IDisposable
    {
        private readonly TcpListener _tcp;
        private readonly UdpClient _udp;
        private readonly CancellationTokenSource _cts = new(TimeSpan.FromSeconds(20));

        public FakePrimary(
            Func<DnsMessage, byte[], int, Task<List<DnsMessage>>> tcpHandler,
            Func<DnsMessage, byte[], int, Task<DnsMessage>> udpHandler
        )
        {
            // Bind TCP to a free port, then UDP to the same number; retry when UDP is taken.
            while (true)
            {
                TcpListener tcp = new(IPAddress.Loopback, 0);
                tcp.Start();
                int candidate = ((IPEndPoint)tcp.LocalEndpoint).Port;

                try
                {
                    _udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, candidate));
                    _tcp = tcp;
                    Port = candidate;
                    break;
                }
                catch (SocketException)
                {
                    tcp.Stop();
                }
            }

            _ = Task.Run(() => UdpLoopAsync(udpHandler));
            _ = Task.Run(() => TcpLoopAsync(tcpHandler));
        }

        public int Port { get; }

        private async Task UdpLoopAsync(Func<DnsMessage, byte[], int, Task<DnsMessage>> handler)
        {
            try
            {
                while (true)
                {
                    UdpReceiveResult received = await _udp.ReceiveAsync(_cts.Token);
                    DnsMessage query = DnsMessageReader.Read(received.Buffer, out int offset);
                    DnsMessage reply = await handler(query, received.Buffer, offset);
                    await _udp.SendAsync(
                        DnsMessageWriter.Write(reply),
                        received.RemoteEndPoint,
                        _cts.Token
                    );
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        private async Task TcpLoopAsync(
            Func<DnsMessage, byte[], int, Task<List<DnsMessage>>> handler
        )
        {
            try
            {
                while (true)
                {
                    using TcpClient accepted = await _tcp.AcceptTcpClientAsync(_cts.Token);
                    await using NetworkStream stream = accepted.GetStream();
                    byte[] prefix = new byte[2];
                    await stream.ReadExactlyAsync(prefix, _cts.Token);
                    byte[] raw = new byte[(prefix[0] << 8) | prefix[1]];
                    await stream.ReadExactlyAsync(raw, _cts.Token);
                    DnsMessage query = DnsMessageReader.Read(raw, out int offset);

                    foreach (DnsMessage message in await handler(query, raw, offset))
                    {
                        byte[] bytes = DnsMessageWriter.Write(message);
                        await stream.WriteAsync(
                            new[] { (byte)(bytes.Length >> 8), (byte)bytes.Length },
                            _cts.Token
                        );
                        await stream.WriteAsync(bytes, _cts.Token);
                    }
                }
            }
            catch (Exception exception)
                when (exception is OperationCanceledException or ObjectDisposedException) { }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _tcp.Stop();
            _udp.Dispose();
        }
    }

    private static Task<List<DnsMessage>> NoTcp(DnsMessage query, byte[] raw, int offset) =>
        Task.FromResult<List<DnsMessage>>([]);

    private static Task<DnsMessage> NoUdp(DnsMessage query, byte[] raw, int offset) =>
        throw new InvalidOperationException("No UDP query expected.");

    private TcpZoneTransferClient CreateClient(int port) =>
        new(
            _scope.ServiceProvider.GetRequiredService<ITsigService>(),
            new StaticOptionsMonitor<ZoneTransferClientOptions>(
                new ZoneTransferClientOptions
                {
                    PrimaryPort = port,
                    ConnectTimeout = TimeSpan.FromSeconds(5),
                    ReadTimeout = TimeSpan.FromSeconds(5),
                    SoaQueryTimeout = TimeSpan.FromSeconds(2),
                }
            )
        );

    /// <inheritdoc />
    public void Dispose() => _scope.Dispose();

    private DnsMessage Answer(DnsMessage query, params DnsResourceRecord[] answers)
    {
        DnsMessage reply = new()
        {
            Id = query.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            IsAuthoritativeAnswer = true,
            Questions = { query.Questions[0] },
        };
        reply.Answers.AddRange(answers);
        return reply;
    }

    private static DnsResourceRecord SoaRecord(uint serial) =>
        new(
            Zone,
            DnsResourceRecordTypeEnum.Soa,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.Soa,
                new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
            )
        );

    private static DnsResourceRecord NsRecord(string nameServer) =>
        new(
            Zone,
            DnsResourceRecordTypeEnum.NS,
            DnsClassEnum.Internet,
            3600,
            DnsRecordDataCodecRegistry.Encode(
                DnsResourceRecordTypeEnum.NS,
                new NsRecordData(DnsName.Parse(nameServer))
            )
        );

    // The ways a fake primary can sign an answer to a signed query.
    public enum SigningMode
    {
        Correct,
        Unsigned,
        OtherKey,
        WrongRequestMac,
    }

    private Task<DnsMessage> SignAnswerAsync(
        DnsMessage reply,
        DnsMessage query,
        string keyName,
        string otherKeyName,
        SigningMode mode
    ) =>
        mode switch
        {
            SigningMode.Unsigned => Task.FromResult(reply),
            SigningMode.OtherKey => _host.WithTsigAsync(tsigService =>
                tsigService.SignResponseAsync(reply, otherKeyName, reply.Id, query.Tsig.Mac)
            ),
            SigningMode.WrongRequestMac => _host.WithTsigAsync(tsigService =>
                tsigService.SignResponseAsync(reply, keyName, reply.Id, new byte[32])
            ),
            _ => _host.WithTsigAsync(tsigService =>
                tsigService.SignResponseAsync(reply, keyName, reply.Id, query.Tsig.Mac)
            ),
        };

    [Fact]
    public async Task PullAsync_AThreeMessageAxfrFromTheRealService_Succeeds()
    {
        string key = await _host.AddKeyAsync();
        using FakePrimary primary = new(
            (query, raw, offset) => _host.TransferAsync(query, raw, offset),
            NoUdp
        );

        ZoneTransferPullResult result = await CreateClient(primary.Port)
            .PullAsync(IPAddress.Loopback, Zone, null, key);

        Assert.True(result.Succeeded, result.FailureReason);
        // The SOA and the five A records; the closing SOA is dropped.
        Assert.Equal(6, result.FullRecordSet.Count);
    }

    [Fact]
    public async Task PullAsync_AResponseSignedWithAnotherEnabledKey_FailsWithBadKey()
    {
        string requestKey = await _host.AddKeyAsync();
        string otherKey = await _host.AddKeyAsync();

        // The primary re-signs the whole transfer with otherKey; every MAC is valid for it.
        using FakePrimary primary = new(
            async (query, raw, offset) =>
            {
                (DnsMessage other, byte[] otherRaw, int otherOffset) = await _host.SignedQueryAsync(
                    TsigTransferHost.ZoneName,
                    DnsResourceRecordTypeEnum.Axfr,
                    otherKey
                );
                return await _host.TransferAsync(other, otherRaw, otherOffset);
            },
            NoUdp
        );

        ZoneTransferPullResult result = await CreateClient(primary.Port)
            .PullAsync(IPAddress.Loopback, Zone, null, requestKey);

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(DnsResponseCodeEnum.BadKey), result.FailureReason);
    }

    [Fact]
    public async Task PullAsync_APrimaryThatRejectsTheSignature_NamesTheTsigError()
    {
        string key = await _host.AddKeyAsync();
        using FakePrimary primary = new(
            (query, raw, offset) =>
            {
                raw[13] ^= 0x20;
                DnsMessage damaged = DnsMessageReader.Read(raw, out int damagedOffset);
                return _host.TransferAsync(damaged, raw, damagedOffset);
            },
            NoUdp
        );

        ZoneTransferPullResult result = await CreateClient(primary.Port)
            .PullAsync(IPAddress.Loopback, Zone, null, key);

        Assert.False(result.Succeeded);
        Assert.Contains(nameof(DnsResponseCodeEnum.BadVersionOrBadSignature), result.FailureReason);
    }

    [Theory]
    [InlineData(SigningMode.Correct, true)]
    [InlineData(SigningMode.Unsigned, false)]
    [InlineData(SigningMode.OtherKey, false)]
    [InlineData(SigningMode.WrongRequestMac, false)]
    public async Task QuerySoaSerialAsync_WithAKey_AcceptsOnlyAnAnswerSignedFromTheQueryMac(
        SigningMode mode,
        bool accepted
    )
    {
        string key = await _host.AddKeyAsync();
        string otherKey = await _host.AddKeyAsync();
        using FakePrimary primary = new(
            NoTcp,
            (query, raw, offset) =>
                SignAnswerAsync(Answer(query, SoaRecord(42)), query, key, otherKey, mode)
        );

        uint? serial = await CreateClient(primary.Port)
            .QuerySoaSerialAsync(IPAddress.Loopback, Zone, key);

        Assert.Equal(accepted ? 42u : null, serial);
    }

    [Theory]
    [InlineData(SigningMode.Correct, true)]
    [InlineData(SigningMode.Unsigned, false)]
    [InlineData(SigningMode.OtherKey, false)]
    [InlineData(SigningMode.WrongRequestMac, false)]
    public async Task PullNsRecordsAsync_WithAKey_AcceptsOnlyAnAnswerSignedFromTheQueryMac(
        SigningMode mode,
        bool accepted
    )
    {
        string key = await _host.AddKeyAsync();
        string otherKey = await _host.AddKeyAsync();
        using FakePrimary primary = new(
            NoTcp,
            (query, raw, offset) =>
                SignAnswerAsync(
                    Answer(query, NsRecord("ns1.stub.test.")),
                    query,
                    key,
                    otherKey,
                    mode
                )
        );

        StubZoneNsPullResult result = await _host.WithTsigAsync(tsigService =>
            StubZoneNsPuller.PullNsRecordsAsync(
                IPAddress.Loopback,
                Zone,
                key,
                primary.Port,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2),
                tsigService
            )
        );

        Assert.Equal(accepted, result.Succeeded);
    }

    [Fact]
    public async Task PullNsRecordsAsync_WithoutAKey_AcceptsAnUnsignedAnswerUnverified()
    {
        using FakePrimary primary = new(
            NoTcp,
            (query, raw, offset) => Task.FromResult(Answer(query, NsRecord("ns1.stub.test.")))
        );

        StubZoneNsPullResult result = await _host.WithTsigAsync(tsigService =>
            StubZoneNsPuller.PullNsRecordsAsync(
                IPAddress.Loopback,
                Zone,
                tsigKeyName: null,
                primary.Port,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(2),
                tsigService
            )
        );

        Assert.True(result.Succeeded, result.FailureReason);
    }
}
