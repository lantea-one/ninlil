using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class tests the provider-name rule of <see cref="DnsCryptCertificateDiscoveryResponder" />
///     (the <c>2.dnscrypt-cert.</c> prefix is optional, case-insensitive and never doubled), the
///     default port, and what the UDP and TCP listeners do with a provider name that cannot be
///     parsed. The name tests call the internal responder directly; the listener tests start real
///     listeners on a free loopback port.
/// </summary>
public sealed class DnsCryptProviderNameTests
{
    [Theory]
    [InlineData("resolver.example", "2.dnscrypt-cert.resolver.example.")]
    [InlineData("2.dnscrypt-cert.resolver.example.", "2.dnscrypt-cert.resolver.example.")]
    [InlineData("2.dnscrypt-cert.resolver.example", "2.dnscrypt-cert.resolver.example.")]
    [InlineData("2.DNSCrypt-Cert.resolver.example", "2.dnscrypt-cert.resolver.example.")]
    public void BuildProviderCertificateName_WithOrWithoutThePrefix_PublishesAtTheSameName(
        string providerName,
        string expected
    )
    {
        DnsName name = DnsCryptCertificateDiscoveryResponder.BuildProviderCertificateName(
            providerName
        );

        // DnsName equality ignores ASCII case, so the expected name matches any spelling.
        Assert.Equal(DnsName.Parse(expected), name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("a..b")]
    public void TryBuildProviderCertificateName_ForABlankOrUnparseableName_ReturnsFalse(
        string providerName
    )
    {
        Assert.False(
            DnsCryptCertificateDiscoveryResponder.TryBuildProviderCertificateName(
                providerName,
                out DnsName name
            )
        );
        Assert.Null(name);
    }

    [Fact]
    public void TryBuildProviderCertificateName_WhenThePrefixMakesTheNameTooLong_ReturnsFalse()
    {
        // 61 + 61 + 61 + 61 octets of labels plus length bytes is 252 on the wire: valid alone,
        // but the 16 prefix octets push it past 255.
        string label = new('a', 61);
        string providerName = $"{label}.{label}.{label}.{label}";

        Assert.True(DnsName.TryParse(providerName, out _));
        Assert.False(
            DnsCryptCertificateDiscoveryResponder.TryBuildProviderCertificateName(
                providerName,
                out _
            )
        );
    }

    [Fact]
    public void TryBuildProviderCertificateName_ForAValidName_ReturnsTheDiscoveryName()
    {
        Assert.True(
            DnsCryptCertificateDiscoveryResponder.TryBuildProviderCertificateName(
                "resolver.example",
                out DnsName name
            )
        );
        Assert.Equal(DnsName.Parse("2.dnscrypt-cert.resolver.example"), name);
    }

    [Fact]
    public void Options_Port_DefaultsTo5443()
    {
        Assert.Equal(5443, new DnsCryptOptions().Port);
    }

    [Fact]
    public async Task Udp_AProviderNameWithThePrefix_AnswersDiscoveryForThatName()
    {
        await using RunningListeners running = await RunningListeners.StartAsync(
            "2.dnscrypt-cert.example.test"
        );

        using UdpClient client = new();
        client.Connect(IPAddress.Loopback, running.Port);

        DnsMessage query = new() { Id = 7, Opcode = DnsOpcodeEnum.Query };
        query.Questions.Add(
            new DnsQuestion(
                DnsName.Parse("2.dnscrypt-cert.example.test"),
                DnsResourceRecordTypeEnum.Txt,
                DnsClassEnum.Internet
            )
        );
        await client.SendAsync(DnsMessageWriter.Write(query));

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        UdpReceiveResult reply = await client.ReceiveAsync(timeout.Token);

        Assert.Equal(
            2,
            DnsCryptWireTestHelper.ExtractCertificatesFromDiscoveryResponse(reply.Buffer).Count
        );
    }

    [Fact]
    public async Task Listeners_AnUnparseableProviderName_LogAnErrorDoNotBindAndDoNotFault()
    {
        CapturingLogger<DnsCryptUdpListener> udpLogger = new();
        CapturingLogger<DnsCryptTcpListener> tcpLogger = new();

        await using RunningListeners running = await RunningListeners.StartAsync(
            "a..b",
            udpLogger,
            tcpLogger
        );

        Assert.Single(udpLogger.Errors);
        Assert.Contains("a..b", udpLogger.Errors[0]);
        Assert.Single(tcpLogger.Errors);
        Assert.False(running.Udp.IsListening);
        Assert.False(running.Tcp.IsListening);

        // The loops have returned without an exception, so the hosted services are completed,
        // not faulted.
        Assert.Equal(TaskStatus.RanToCompletion, running.Udp.ExecuteTask.Status);
        Assert.Equal(TaskStatus.RanToCompletion, running.Tcp.ExecuteTask.Status);

        // Nothing is bound to the port, so a connect is refused.
        using TcpClient client = new();
        await Assert.ThrowsAsync<SocketException>(() =>
            client.ConnectAsync(IPAddress.Loopback, running.Port)
        );
    }

    /// <summary>
    ///     This class starts a pipeline plus both DNSCrypt listeners with one provider name on a
    ///     free loopback port, with signed certificates for both ciphers, and stops them when
    ///     disposed.
    /// </summary>
    private sealed class RunningListeners : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;

        /// <summary>This property contains the UDP listener under test.</summary>
        public DnsCryptUdpListener Udp { get; private set; }

        /// <summary>This property contains the TCP listener under test.</summary>
        public DnsCryptTcpListener Tcp { get; private set; }

        /// <summary>This property contains the port both listeners were told to bind.</summary>
        public int Port { get; private set; }

        /// <summary>
        ///     This method starts both listeners for <paramref name="providerName" /> and gives
        ///     their background loops a moment to bind or give up.
        /// </summary>
        public static async Task<RunningListeners> StartAsync(
            string providerName,
            ILogger<DnsCryptUdpListener> udpLogger = null,
            ILogger<DnsCryptTcpListener> tcpLogger = null
        )
        {
            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    DnssecAlgorithmEnum.Ed25519,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                throw new InvalidOperationException("Ed25519 is not registered.");

            DnssecKeyPair providerKeyPair = algorithm.GenerateKeyPair();
            InMemoryDnsCryptCertificateStore store = new(providerKeyPair);
            store.Save([
                DnsCryptListenerTestFixture.BuildSignedCertificate(
                    algorithm,
                    providerKeyPair,
                    DnsCryptEsVersion.XChaCha20Poly1305
                ),
                DnsCryptListenerTestFixture.BuildSignedCertificate(
                    algorithm,
                    providerKeyPair,
                    DnsCryptEsVersion.XSalsa20Poly1305
                ),
            ]);

            RunningListeners running = new() { Port = LocalAddresses.GetFreePort() };
            StaticOptionsMonitor<DnsCryptOptions> options = new(
                new DnsCryptOptions
                {
                    Enable = true,
                    Port = running.Port,
                    ProviderName = providerName,
                    ListenAddresses = "127.0.0.1",
                }
            );

            running._pipeline = new DnsQueryProcessingPipeline(
                new DnsQueryDispatcher(
                    [new EchoResolver()],
                    new DnsServerEventPublisher(
                        Options.Create(new DnsServerEventPublisherOptions()),
                        NullLogger<DnsServerEventPublisher>.Instance
                    )
                ),
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await running._pipeline.StartAsync(CancellationToken.None);

            running.Udp = new DnsCryptUdpListener(
                running._pipeline,
                store,
                options,
                udpLogger ?? NullLogger<DnsCryptUdpListener>.Instance
            );
            running.Tcp = new DnsCryptTcpListener(
                running._pipeline,
                store,
                options,
                tcpLogger ?? NullLogger<DnsCryptTcpListener>.Instance
            );
            await running.Udp.StartAsync(CancellationToken.None);
            await running.Tcp.StartAsync(CancellationToken.None);

            // Binding happens on each listener's background loop, just after StartAsync returns.
            await Task.Delay(300);
            return running;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Udp is not null)
                await Udp.StopAsync(CancellationToken.None);

            if (Tcp is not null)
                await Tcp.StopAsync(CancellationToken.None);

            if (_pipeline is not null)
                await _pipeline.StopAsync(CancellationToken.None);
        }
    }
}
