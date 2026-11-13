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

        await using RunningListeners running = await RunningListeners.StartAsync(
            "127.0.0.1",
            expectedFailedAddresses: 0
        );

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
            expectedFailedAddresses: 1,
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
    ///     given listen addresses, and stops them all when disposed. Port 0 cannot serve these
    ///     tests: UDP and TCP, and every listen address, must share one port number, but port 0
    ///     gives each socket a port of its own. The number is therefore reserved first and the
    ///     listeners bind it after it is released, so another process can take it in between;
    ///     <see cref="StartAsync" /> then starts again on a new number, a bounded number of times.
    /// </summary>
    private sealed class RunningListeners : IAsyncDisposable
    {
        private DnsQueryProcessingPipeline _pipeline;

        public DnsCryptUdpListener Udp { get; private set; }

        public DnsCryptTcpListener Tcp { get; private set; }

        public int Port { get; private set; }

        /// <summary>
        ///     This constant is how many reserved ports <see cref="StartAsync" /> tries before it
        ///     gives up.
        /// </summary>
        private const int BindAttempts = 5;

        /// <param name="listenAddresses">The configured <c>ListenAddresses</c> value.</param>
        /// <param name="expectedFailedAddresses">
        ///     How many of the addresses cannot bind by design (such as an unassigned one). Any
        ///     other failure means the reserved port was taken, and the listeners start again on
        ///     another port.
        /// </param>
        /// <param name="loggers">Where the listeners log to; nowhere when omitted.</param>
        public static async Task<RunningListeners> StartAsync(
            string listenAddresses,
            int expectedFailedAddresses,
            CapturingLoggerFactory loggers = null
        )
        {
            for (int attempt = 1; ; attempt++)
            {
                RunningListeners running = await StartOnceAsync(listenAddresses, loggers);

                if (
                    running.Udp.IsListening
                    && running.Tcp.IsListening
                    && ExpectedFailuresOnly(
                        running.Udp.FailedListenAddresses,
                        expectedFailedAddresses
                    )
                    && ExpectedFailuresOnly(
                        running.Tcp.FailedListenAddresses,
                        expectedFailedAddresses
                    )
                )
                    return running;

                // The reserved port was taken between the reservation and the bind. The
                // loggers keep the failed attempt's errors; the tests only look for an error
                // that names the unassigned address, which every attempt logs.
                await running.DisposeAsync();

                if (attempt == BindAttempts)
                    throw new InvalidOperationException(
                        "The DNSCrypt listeners did not bind on any reserved port."
                    );
            }
        }

        /// <summary>
        ///     This method reports whether the failed addresses are exactly the expected ones: as
        ///     many as <paramref name="expectedFailedAddresses" />, all of them the unassigned
        ///     address. Any other failure is a bind that should have worked.
        /// </summary>
        private static bool ExpectedFailuresOnly(
            IReadOnlyList<string> failed,
            int expectedFailedAddresses
        ) =>
            failed.Count == expectedFailedAddresses
            && failed.All(entry => entry.StartsWith($"{LocalAddresses.Unassigned} ("));

        /// <summary>
        ///     This method makes one attempt: it reserves a port, starts the pipeline and both
        ///     listeners on it, and returns them whether or not they bound.
        /// </summary>
        private static async Task<RunningListeners> StartOnceAsync(
            string listenAddresses,
            CapturingLoggerFactory loggers
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

            // See the class summary: the number is reserved first (LocalAddresses.GetFreePort).
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
            await DnsCryptListenerStartup.WaitForBindOutcomeAsync(running.Udp, running.Tcp);
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
