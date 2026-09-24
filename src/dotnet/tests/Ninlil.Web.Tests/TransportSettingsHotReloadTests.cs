using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Protocol.DnsCrypt;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.DnsCrypt;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class proves, directly against the real .NET configuration/options binding pipeline
///     and a real, already-constructed <see cref="DnsCryptUdpListener" /> instance -- the same
///     shape <c>RecursiveOptionsHotReloadTests</c> already established for
///     <see cref="Ninlil.Dns.Server.Recursive.RecursiveServerOptions" /> -- that a write
///     <see cref="Controllers.TransportSettingsController.PatchDnsCrypt" /> itself makes via
///     <see cref="PresetConfigurationFileApplier" /> genuinely reaches an already-running listener
///     with no restart: this test constructs the listener once, confirms it declines to bind while
///     <c>Enable</c> is <see langword="false" />, writes a fresh <c>Enable</c>/<c>Port</c>/
///     <c>ProviderName</c> through the exact same merge-patch mechanism the controller calls, calls
///     the identical listener instance's own <c>StartAsync</c> again, and confirms it now genuinely
///     binds and answers a real, plaintext certificate-discovery UDP packet.
///     <para>
///         Deliberately run here, standalone (its own <see cref="IConfigurationRoot" />/
///         <see cref="ServiceProvider" />, no <see cref="NinlilDnsWebApplicationFactory" />
///         involved), rather than as part of <c>TransportSettingsControllerTests</c>: that shared
///         collection's own factory instance points every runtime settings write at an isolated
///         file that is deliberately not one of its own host's watched
///         <see cref="IConfiguration" /> sources (see that factory's own doc comment), so
///         <see cref="IOptionsMonitor{TOptions}" /> never actually observes a write made that way
///         inside that specific test process -- this class exists precisely to prove the real
///         mechanism still works, against the real configuration-reload pipeline a production
///         deployment (where the runtime settings file <em>is</em> the same, already-watched
///         <c>appsettings.json</c>) actually has.
///     </para>
/// </summary>
public class TransportSettingsHotReloadTests
{
    /// <summary>
    ///     This method builds a plaintext certificate-discovery query for a given provider name.
    /// </summary>
    private static byte[] BuildDiscoveryQuery(string providerName) =>
        DnsMessageWriter.Write(
            new DnsMessage
            {
                Id = 7,
                Opcode = DnsOpcodeEnum.Query,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse($"2.dnscrypt-cert.{providerName}"),
                        DnsResourceRecordTypeEnum.Txt,
                        DnsClassEnum.Internet
                    ),
                },
            }
        );

    /// <summary>
    ///     This method finds an OS-assigned, currently-free UDP port to bind this test's own
    ///     listener against.
    /// </summary>
    private static int GetFreeUdpPort()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint).Port;
    }

    /// <summary>
    ///     This method sends a single UDP packet at <c>127.0.0.1:<paramref name="port" /></c> and
    ///     waits briefly for a reply, returning <see langword="null" /> instead of throwing when
    ///     none arrives in time.
    /// </summary>
    private static async Task<byte[]> TrySendAndReceiveAsync(byte[] requestBytes, int port)
    {
        try
        {
            using UdpClient client = new();
            client.Connect(IPAddress.Loopback, port);
            await client.SendAsync(requestBytes);

            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(2));
            UdpReceiveResult result = await client.ReceiveAsync(timeout.Token);
            return result.Buffer;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_MakesTheSameDnsCryptListenerInstanceGenuinelyStartAnsweringWithNoRestart()
    {
        string configPath = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-dnscrypt-hot-reload-{Guid.NewGuid():N}.json"
        );
        string certificateStatePath = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-dnscrypt-hot-reload-certs-{Guid.NewGuid():N}.json"
        );
        int port = GetFreeUdpPort();
        const string providerName = "hot-reload-test.invalid.";

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source a production deployment's own appsettings.json is
            // (see this class's own doc comment for why that specifically is not true of the
            // shared WebApplicationFactory used elsewhere in this project's own test suite).
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(configPath, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<DnsCryptOptions>(
                configuration.GetSection(DnsCryptOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<DnsCryptOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<DnsCryptOptions>
            >();

            // Before any write, the file does not exist yet -- this class's own compiled-in
            // default, exactly as a fresh installation would see it: DNSCrypt off.
            Assert.False(monitor.CurrentValue.Enable);

            DnsServerEventPublisher eventPublisher = new(
                Microsoft.Extensions.Options.Options.Create(new DnsServerEventPublisherOptions()),
                NullLogger<DnsServerEventPublisher>.Instance
            );
            DnsQueryDispatcher dispatcher = new([], eventPublisher);
            await using DnsQueryProcessingPipeline pipeline = new(
                dispatcher,
                new StaticOptionsMonitor<DnsQueryProcessingPipelineOptions>(
                    new DnsQueryProcessingPipelineOptions()
                ),
                NullLogger<DnsQueryProcessingPipeline>.Instance
            );
            await pipeline.StartAsync(CancellationToken.None);

            FileDnsCryptCertificateStore store = new(
                Microsoft.Extensions.Options.Options.Create(
                    new DnsCryptOptions { CertificateStateFilePath = certificateStatePath }
                ),
                NullLogger<FileDnsCryptCertificateStore>.Instance
            );

            // Constructed once, exactly as the real DI container constructs it as a singleton --
            // never rebuilt for the rest of this test.
            DnsCryptUdpListener listener = new(
                pipeline,
                store,
                monitor,
                NullLogger<DnsCryptUdpListener>.Instance
            );

            byte[] discoveryQuery = BuildDiscoveryQuery(providerName);

            // Before the write: Enable is false, so this StartAsync call's own ExecuteAsync
            // returns immediately without ever binding a socket -- confirmed by a real UDP send
            // that gets no reply at all, not merely by reading IsListening.
            await listener.StartAsync(CancellationToken.None);
            Assert.Null(await TrySendAndReceiveAsync(discoveryQuery, port));
            await listener.StopAsync(CancellationToken.None);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The exact same write mechanism TransportSettingsController.PatchDnsCrypt itself
            // calls.
            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        DnsCryptOptions.SectionName,
                        nameof(DnsCryptOptions.Enable),
                        true
                    ),
                    new PresetConfigurationSetting(
                        DnsCryptOptions.SectionName,
                        nameof(DnsCryptOptions.Port),
                        port
                    ),
                    new PresetConfigurationSetting(
                        DnsCryptOptions.SectionName,
                        nameof(DnsCryptOptions.ProviderName),
                        providerName
                    ),
                ],
                configPath
            );

            Task completed = await Task.WhenAny(
                reloaded.Task,
                Task.Delay(TimeSpan.FromSeconds(10))
            );
            Assert.Same(reloaded.Task, completed);
            Assert.True(monitor.CurrentValue.Enable);
            Assert.Equal(port, monitor.CurrentValue.Port);
            Assert.Equal(providerName, monitor.CurrentValue.ProviderName);

            // The identical listener instance, constructed before the write, now genuinely binds
            // and answers a real UDP packet -- no restart, no new instance -- proving
            // DnsCryptUdpListener's own IOptions<T> -> IOptionsMonitor<T> conversion is what makes
            // TransportSettingsController's own live enable toggle real, not merely cosmetic.
            await listener.StartAsync(CancellationToken.None);

            // BackgroundService.StartAsync schedules ExecuteAsync's own body (including the
            // actual socket bind) as a continuation rather than running it inline before
            // returning -- called directly here rather than through
            // ManagedServiceRegistration.StartAsync (which polls exactly this signal itself, for
            // exactly this reason), so this test polls it directly instead.
            DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
            while (!listener.IsListening && DateTime.UtcNow < deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(10));
            Assert.True(listener.IsListening);

            try
            {
                byte[] response = await TrySendAndReceiveAsync(discoveryQuery, port);
                Assert.NotNull(response);

                DnsMessage responseMessage = DnsMessageReader.Read(response);
                Assert.True(responseMessage.IsResponse);
                Assert.Equal(7, responseMessage.Id);
            }
            finally
            {
                await listener.StopAsync(CancellationToken.None);
            }

            // Stopped again: the same instance, once more, genuinely stops answering.
            Assert.Null(await TrySendAndReceiveAsync(discoveryQuery, port));
        }
        finally
        {
            foreach (string path in new[] { configPath, certificateStatePath })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup only -- a lingering temp file from a test run is
                    // harmless.
                }
            }
        }
    }
}
