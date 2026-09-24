using System.Buffers;
using System.Buffers.Text;
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.Core.Forwarding;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Forwarding.Clients;
using Ninlil.Dns.Server.Forwarding.Definition;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This class verifies <see cref="DnsCryptForwarderClient" /> and <see cref="DnsCryptStampParser" />
///     end-to-end, against this project's own real <see cref="DnsCryptUdpListener" /> standing in
///     for a real-world upstream DNSCrypt resolver -- the only way to exercise the client role
///     without a live third-party DNSCrypt server, and a genuinely real round trip (real
///     sockets, real X25519/Ed25519 crypto, real certificate discovery) rather than a mock.
/// </summary>
public class DnsCryptForwarderClientTests
{
    private sealed class FixedAnswerDispatcher(DnsMessage response) : IDnsQueryDispatcher
    {
        public ValueTask<DnsMessage> DispatchAsync(
            DnsQueryContext context,
            CancellationToken stoppingToken = default
        )
        {
            DnsMessage answered = new()
            {
                Id = context.Request.Id,
                IsResponse = response.IsResponse,
                Opcode = response.Opcode,
                ResponseCode = response.ResponseCode,
            };
            answered.Answers.AddRange(response.Answers);
            return ValueTask.FromResult(answered);
        }
    }

    private static int GetEphemeralUdpPort()
    {
        using System.Net.Sockets.Socket socket = new(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Dgram,
            System.Net.Sockets.ProtocolType.Udp
        );
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    private static string BuildStamp(string address, byte[] providerPublicKey, string providerName)
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x01;
        buffer.Advance(1);

        Span<byte> props = buffer.GetSpan(8);
        props.Clear();
        buffer.Advance(8);

        WriteLengthPrefixed(buffer, System.Text.Encoding.ASCII.GetBytes(address));
        WriteLengthPrefixed(buffer, providerPublicKey);
        WriteLengthPrefixed(buffer, System.Text.Encoding.UTF8.GetBytes(providerName));

        return "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);
    }

    private static void WriteLengthPrefixed(IBufferWriter<byte> buffer, byte[] value)
    {
        buffer.GetSpan(1)[0] = (byte)value.Length;
        buffer.Advance(1);
        value.CopyTo(buffer.GetSpan(value.Length));
        buffer.Advance(value.Length);
    }

    [Fact]
    public async Task ForwardAsync_CompletesARealEncryptedRoundTripAgainstARealDnsCryptListener()
    {
        int port = GetEphemeralUdpPort();
        string statePath = Path.Combine(
            Path.GetTempPath(),
            $"forwarding-dnscrypt-test-{Guid.NewGuid():N}.json"
        );

        try
        {
            DnsCryptOptions options = new()
            {
                Enable = true,
                Port = port,
                ProviderName = "test.example",
                CertificateStateFilePath = statePath,
                EnabledEsVersions = [DnsCryptEsVersion.XChaCha20Poly1305],
                CertificateRotationInterval = TimeSpan.FromMinutes(10),
                CertificateValidityDuration = TimeSpan.FromMinutes(20),
                CertificateClockSkewTolerance = TimeSpan.Zero,
            };
            IOptions<DnsCryptOptions> optionsWrapped = Options.Create(options);
            IOptionsMonitor<DnsCryptOptions> optionsMonitor =
                new StaticOptionsMonitor<DnsCryptOptions>(options);

            FileDnsCryptCertificateStore store = new(
                optionsWrapped,
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );
            DnsCryptCertificateRotationService rotationService = new(
                store,
                optionsMonitor,
                NullLogger<DnsCryptCertificateRotationService>.Instance
            );
            await rotationService.StartAsync(CancellationToken.None);

            for (int attempt = 0; attempt < 40 && store.Entries.Count < 1; attempt++)
                await Task.Delay(50);

            Assert.NotEmpty(store.Entries);

            DnsMessage cannedResponse = new()
            {
                IsResponse = true,
                Opcode = DnsOpcodeEnum.Query,
                ResponseCode = DnsResponseCodeEnum.NoError,
            };
            cannedResponse.Answers.Add(
                new DnsResourceRecord(
                    DnsName.Parse("forwarded.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet,
                    300,
                    [10, 20, 30, 40]
                )
            );

            DnsQueryProcessingPipeline pipeline = new(
                new FixedAnswerDispatcher(cannedResponse),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await pipeline.StartAsync(CancellationToken.None);

            DnsCryptUdpListener listener = new(
                pipeline,
                store,
                optionsMonitor,
                NullLogger<DnsCryptUdpListener>.Instance
            );
            await listener.StartAsync(CancellationToken.None);
            await Task.Delay(200);

            string stamp = BuildStamp(
                $"127.0.0.1:{port}",
                store.ProviderKeyPair.PublicKeyWireFormat,
                "2.dnscrypt-cert.test.example"
            );

            UpstreamForwarderDefinition definition = DnsCryptStampParser.Parse(stamp);

            Assert.Equal(UpstreamForwarderProtocolEnum.DnsCrypt, definition.Protocol);
            Assert.Equal(IPAddress.Loopback, definition.Address);
            Assert.Equal(port, definition.Port);
            Assert.Equal("2.dnscrypt-cert.test.example", definition.DnsCryptProviderName);

            DnsMessage query = new()
            {
                Id = 4242,
                Opcode = DnsOpcodeEnum.Query,
                RecursionDesired = true,
            };
            query.Questions.Add(
                new DnsQuestion(
                    DnsName.Parse("forwarded.example."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                )
            );

            DnsCryptForwarderClient client = new();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));

            DnsMessage response = await client.ForwardAsync(definition, query, timeout.Token);

            Assert.Equal(4242, response.Id);
            Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
            Assert.Single(response.Answers);
            Assert.Equal(new IPAddress([10, 20, 30, 40]), new IPAddress(response.Answers[0].Data));

            await listener.StopAsync(CancellationToken.None);
            await pipeline.StopAsync(CancellationToken.None);
            await rotationService.StopAsync(CancellationToken.None);
        }
        finally
        {
            if (File.Exists(statePath))
                File.Delete(statePath);
        }
    }

    [Fact]
    public void Parse_RejectsAStampWithTheWrongProtocolIdentifier()
    {
        ArrayBufferWriter<byte> buffer = new();
        buffer.GetSpan(1)[0] = 0x02;
        buffer.Advance(1);

        string stamp = "sdns://" + Base64Url.EncodeToString(buffer.WrittenSpan);

        Assert.Throws<FormatException>(() => DnsCryptStampParser.Parse(stamp));
    }
}
