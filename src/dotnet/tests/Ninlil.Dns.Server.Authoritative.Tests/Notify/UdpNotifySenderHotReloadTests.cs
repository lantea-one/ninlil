using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Notify;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Notify;

/// <summary>
///     This class proves, against real loopback UDP sockets and a real reload-on-change JSON
///     configuration file -- not merely <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read
///     in isolation -- that a write reaching <see cref="NotifyOptions" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.NotifySettingsController.Patch</c> itself calls) changes
///     which port <see cref="UdpNotifySender.SendAsync" /> targets on its very next attempt, with
///     no restart. Before this fix, <see cref="UdpNotifySender" /> read <see cref="NotifyOptions" />
///     via the usual, snapshotted <see cref="IOptions{TOptions}" />, computed once and cached for
///     the life of the process -- a write reaching the configuration file would never have been
///     observed at all, the same shape this project's other options-bound classes each already
///     found and fixed for their own options class.
/// </summary>
public sealed class UdpNotifySenderHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesWhichPortTheVerySendAsyncCallTargets_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-notify-secondary-port-hot-reload-{Guid.NewGuid():N}.json"
        );

        using UdpClient firstSecondary = new(new IPEndPoint(IPAddress.Loopback, 0));
        using UdpClient secondSecondary = new(new IPEndPoint(IPAddress.Loopback, 0));
        int firstPort = ((IPEndPoint)firstSecondary.Client.LocalEndPoint).Port;
        int secondPort = ((IPEndPoint)secondSecondary.Client.LocalEndPoint).Port;

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<NotifyOptions>(configuration.GetSection(NotifyOptions.SectionName));
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<NotifyOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<NotifyOptions>
            >();

            // Seed SecondaryPort at the first loopback listener's own ephemeral port.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        NotifyOptions.SectionName,
                        nameof(NotifyOptions.SecondaryPort),
                        firstPort
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        NotifyOptions.SectionName,
                        nameof(NotifyOptions.MaxAttempts),
                        1
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        NotifyOptions.SectionName,
                        nameof(NotifyOptions.ResponseTimeout),
                        "00:00:05"
                    ),
                ],
                path
            );

            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.SecondaryPort == firstPort
            );

            UdpNotifySender sender = new(monitor);

            DnsMessage notify = new()
            {
                Id = 123,
                Opcode = DnsOpcodeEnum.Notify,
                Questions =
                {
                    new DnsQuestion(
                        DnsName.Parse("example.test."),
                        DnsResourceRecordTypeEnum.Soa,
                        DnsClassEnum.Internet
                    ),
                },
            };

            Task<bool> firstSendTask = sender.SendAsync(IPAddress.Loopback, notify);
            await RespondToNextNotifyAsync(firstSecondary);
            Assert.True(await firstSendTask);

            // The exact same write mechanism NotifySettingsController.Patch itself calls, this
            // time pointing SecondaryPort at the second loopback listener instead.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        NotifyOptions.SectionName,
                        nameof(NotifyOptions.SecondaryPort),
                        secondPort
                    ),
                ],
                path
            );

            // Polls the actual settled value directly rather than trusting a single OnChange
            // firing to mean the reload is fully applied -- a second write to the same monitor
            // within one test can otherwise race a stray/delayed notification left over from the
            // first write against this one, observed directly as a real, reproducible flake.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.SecondaryPort == secondPort
            );

            // The identical sender instance, constructed before the write, now genuinely targets
            // the second listener -- proof the port was actually re-read rather than captured
            // once at construction.
            Task<bool> secondSendTask = sender.SendAsync(IPAddress.Loopback, notify);
            await RespondToNextNotifyAsync(secondSecondary);
            Assert.True(await secondSendTask);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Best-effort cleanup only -- a lingering temp file from a test run is harmless.
            }
        }
    }

    /// <summary>
    ///     This method waits for a real configuration reload to actually happen (.NET's own JSON
    ///     configuration provider debounces file-change notifications by a few hundred
    ///     milliseconds) rather than asserting against a fixed delay.
    /// </summary>
    private static async Task WaitForReloadAsync(
        IOptionsMonitor<NotifyOptions> monitor,
        Func<bool> condition
    )
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50);

        Assert.True(condition(), "The configuration reload did not happen within the timeout.");
    }

    /// <summary>
    ///     This method waits for a NOTIFY datagram on the given loopback listener and answers it
    ///     with a matching acknowledgement, the way a real secondary would.
    /// </summary>
    private static async Task RespondToNextNotifyAsync(UdpClient listener)
    {
        UdpReceiveResult received = await listener
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        DnsMessage request = DnsMessageReader.Read(received.Buffer);

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Notify,
            ResponseCode = DnsResponseCodeEnum.NoError,
        };

        byte[] responseBytes = DnsMessageWriter.Write(response);
        await listener.SendAsync(responseBytes, received.RemoteEndPoint);
    }
}
