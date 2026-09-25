using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Protocol.DnsCrypt.Tests.Fakes;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Dnssec.Algorithms;

// Define our namespace.
namespace Ninlil.Dns.Protocol.DnsCrypt.Tests;

/// <summary>
///     This class checks that both DNSCrypt listeners honor <see cref="DnsCryptOptions.ListenAddresses" />.
///     The binding itself is the shared <c>ListenSocketSet</c>, whose query-level behavior
///     (answered on a listed address, silent on any other) is covered end to end by the Plain
///     transport's tests; these tests check that the DNSCrypt listeners use it: they bind only
///     what is listed, and report and log an address they cannot use without giving up the
///     others. Each test starts its own listeners on a free port, separate from
///     <see cref="DnsCryptListenerTestFixture" />'s shared ones.
/// </summary>
public sealed class DnsCryptListenAddressTests
{
    [Fact]
    public async Task LoopbackOnly_TcpIsReachableThereButNotOnAnotherLocalAddress()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        await using RunningListeners running = await RunningListeners.StartAsync("127.0.0.1");

        Assert.True(running.Udp.IsListening);
        Assert.True(running.Tcp.IsListening);
        Assert.Empty(running.Udp.FailedListenAddresses);
        Assert.Empty(running.Tcp.FailedListenAddresses);

        Assert.True(await CanConnectAsync(IPAddress.Loopback, running.Port));
        Assert.False(await CanConnectAsync(other, running.Port));
    }

    [Fact]
    public async Task ABadAddress_IsLoggedAndReported_WhileTheOthersKeepListening()
    {
        IPAddress other = LocalAddresses.OtherLoopback();

        if (other is null)
            return;

        CapturingLoggerFactory loggers = new();
        await using RunningListeners running = await RunningListeners.StartAsync(
            $"127.0.0.1, {LocalAddresses.Unassigned}, {other}",
            loggers
        );

        Assert.True(running.Udp.IsListening);
        Assert.True(running.Tcp.IsListening);
        Assert.True(await CanConnectAsync(IPAddress.Loopback, running.Port));
        Assert.True(await CanConnectAsync(other, running.Port));

        Assert.StartsWith("192.0.2.1 (", Assert.Single(running.Udp.FailedListenAddresses));
        Assert.StartsWith("192.0.2.1 (", Assert.Single(running.Tcp.FailedListenAddresses));
        Assert.Contains(loggers.UdpLogger.Errors, message => message.Contains("192.0.2.1"));
        Assert.Contains(loggers.TcpLogger.Errors, message => message.Contains("192.0.2.1"));
    }

    /// <summary>
    ///     This method reports whether a TCP connection to an address and port succeeds -- a
    ///     refused connection means nothing listens there.
    /// </summary>
    private static async Task<bool> CanConnectAsync(IPAddress address, int port)
    {
        using TcpClient client = new(address.AddressFamily);
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));

        try
        {
            await client.ConnectAsync(address, port, timeout.Token);
            return true;
        }
        catch (Exception exception)
            when (exception is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This class hands each listener its own <see cref="CapturingLogger{T}" />, so a test
    ///     can check what each one logged.
    /// </summary>
    private sealed class CapturingLoggerFactory
    {
        public CapturingLogger<DnsCryptUdpListener> UdpLogger { get; } = new();

        public CapturingLogger<DnsCryptTcpListener> TcpLogger { get; } = new();
    }

    /// <summary>
    ///     This class starts a pipeline plus both DNSCrypt listeners on a free port with the
    ///     given listen addresses, and stops them all when disposed.
    /// </summary>
    private sealed class RunningListeners : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;

        public DnsCryptUdpListener Udp { get; private set; }

        public DnsCryptTcpListener Tcp { get; private set; }

        public int Port { get; private set; }

        /// <param name="listenAddresses">The configured <c>ListenAddresses</c> value.</param>
        /// <param name="loggers">Where the listeners log to; nowhere when omitted.</param>
        public static async Task<RunningListeners> StartAsync(
            string listenAddresses,
            CapturingLoggerFactory loggers = null
        )
        {
            ILogger<DnsCryptUdpListener> udpLogger = loggers is null
                ? NullLogger<DnsCryptUdpListener>.Instance
                : loggers.UdpLogger;
            ILogger<DnsCryptTcpListener> tcpLogger = loggers is null
                ? NullLogger<DnsCryptTcpListener>.Instance
                : loggers.TcpLogger;

            if (
                !DnssecAlgorithmRegistry.TryGetAlgorithm(
                    DnssecAlgorithmEnum.Ed25519,
                    out IDnssecSigningAlgorithm algorithm
                )
            )
                throw new InvalidOperationException("Ed25519 is not registered.");

            // The listeners only read certificates when answering a certificate query, which
            // these tests never send, so an empty store is enough.
            InMemoryDnsCryptCertificateStore store = new(algorithm.GenerateKeyPair());

            RunningListeners running = new() { Port = LocalAddresses.GetFreePort() };
            StaticOptionsMonitor<DnsCryptOptions> options = new(
                new DnsCryptOptions
                {
                    Enable = true,
                    Port = running.Port,
                    ProviderName = DnsCryptListenerTestFixture.ProviderName,
                    ListenAddresses = listenAddresses,
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

            running.Udp = new DnsCryptUdpListener(running._pipeline, store, options, udpLogger);
            running.Tcp = new DnsCryptTcpListener(running._pipeline, store, options, tcpLogger);
            await running.Udp.StartAsync(CancellationToken.None);
            await running.Tcp.StartAsync(CancellationToken.None);

            // Binding happens on each listener's background loop, just after StartAsync returns.
            await Task.Delay(200);
            return running;
        }

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
