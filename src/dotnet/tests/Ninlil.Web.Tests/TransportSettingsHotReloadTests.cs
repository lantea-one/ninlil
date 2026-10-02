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
///     This class checks that a DNSCrypt write made the way
///     <see cref="Controllers.TransportSettingsController.PatchDnsCrypt" /> makes it, through
///     <see cref="PresetConfigurationFileApplier" />, reaches a <see cref="DnsCryptUdpListener" />
///     built before the write, without a restart, as <c>RecursiveOptionsHotReloadTests</c> does
///     for <see cref="Ninlil.Dns.Server.Recursive.RecursiveServerOptions" />. The listener is built
///     once on a minimal pipeline. With <c>Enable</c> off, starting it binds nothing and a UDP
///     query to a free loopback port gets no answer; after <c>Enable</c>, <c>Port</c> and
///     <c>ProviderName</c> are written, the same instance is started again and answers a
///     plaintext certificate-discovery query; after it is stopped it answers nothing.
///     <para>
///         It uses its own <see cref="IConfigurationRoot" /> and <see cref="ServiceProvider" />
///         instead of <see cref="NinlilDnsWebApplicationFactory" />, whose settings file is not a
///         configuration source (see <see cref="AppearanceOptionsHotReloadTests" />), so this is the
///         reload a deployment gets, where the settings file is <c>appsettings.json</c>.
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
    ///     This method finds a free UDP port on loopback for the listener.
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
            // A reload-on-change JSON file source, the kind appsettings.json is in a deployment.
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

            // The file does not exist yet, so this is the compiled-in default: DNSCrypt off.
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

            // Constructed once, as the singleton is, and never rebuilt during the test.
            DnsCryptUdpListener listener = new(
                pipeline,
                store,
                monitor,
                NullLogger<DnsCryptUdpListener>.Instance
            );

            byte[] discoveryQuery = BuildDiscoveryQuery(providerName);

            // Enable is false, so ExecuteAsync returns at once without binding a socket. A UDP
            // query gets no reply, which is checked rather than IsListening.
            await listener.StartAsync(CancellationToken.None);
            Assert.Null(await TrySendAndReceiveAsync(discoveryQuery, port));
            await listener.StopAsync(CancellationToken.None);

            TaskCompletionSource reloaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IDisposable subscription = monitor.OnChange(_ => reloaded.TrySetResult());

            // The same call TransportSettingsController.PatchDnsCrypt makes.
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

            // The listener built before the write now binds and answers, with no restart or new
            // instance, because DnsCryptUdpListener reads its options through IOptionsMonitor.
            await listener.StartAsync(CancellationToken.None);

            // BackgroundService.StartAsync runs ExecuteAsync, socket bind included, as a
            // continuation rather than before returning. ManagedServiceRegistration.StartAsync
            // waits on IsListening for that reason; calling the listener directly, the test polls
            // it itself.
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

            // Once stopped, the same instance no longer answers.
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
                    // Best-effort cleanup; a leftover temporary file is harmless.
                }
            }
        }
    }
}
