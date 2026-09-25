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
///     This class checks, with loopback UDP sockets, that a change to <see cref="NotifyOptions" />
///     written through <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.NotifySettingsController.Patch</c> calls) into a reload-on-change
///     JSON file changes the port the next <see cref="UdpNotifySender.SendAsync" /> uses, without a
///     restart. <see cref="UdpNotifySender" /> must read the options through
///     <see cref="IOptionsMonitor{TOptions}" />; an <see cref="IOptions{TOptions}" /> value is cached
///     for the life of the process.
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
            // A reload-on-change JSON file source, the same kind the web host uses for its
            // configuration files.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<NotifyOptions>(configuration.GetSection(NotifyOptions.SectionName));
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<NotifyOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<NotifyOptions>
            >();

            // Seed SecondaryPort at the first loopback listener's ephemeral port.
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

            // The same write NotifySettingsController.Patch makes, pointing SecondaryPort at the second
            // listener.
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

            // Poll the value itself rather than trusting one OnChange: a late notification from the
            // first write can fire first and made this test flaky.
            await WaitForReloadAsync(
                monitor,
                () => monitor.CurrentValue.SecondaryPort == secondPort
            );

            // The same sender, built before the write, now uses the second listener, so the port is
            // re-read per call.
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
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }

    /// <summary>
    ///     The JSON provider debounces file-change notifications, so wait for the reload event
    ///     rather than a fixed delay.
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
