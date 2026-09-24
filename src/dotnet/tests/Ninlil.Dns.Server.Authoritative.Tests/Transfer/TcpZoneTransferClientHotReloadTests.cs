using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Tsig;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class proves, against real loopback UDP sockets and a real reload-on-change JSON
///     configuration file -- not merely <see cref="IOptionsMonitor{TOptions}.CurrentValue" /> read
///     in isolation -- that a write reaching <see cref="ZoneTransferClientOptions" /> via
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (the exact mechanism
///     <c>Ninlil.Web.Controllers.ZoneTransferClientSettingsController.Patch</c> itself
///     calls) changes which port <see cref="TcpZoneTransferClient.QuerySoaSerialAsync" /> targets
///     on its very next attempt, with no restart. Before this fix,
///     <see cref="TcpZoneTransferClient" /> read <see cref="ZoneTransferClientOptions" /> via the
///     usual, snapshotted <see cref="IOptions{TOptions}" />, computed once and cached for the life
///     of the process -- a write reaching the configuration file would never have been observed at
///     all, the same shape this project's other options-bound classes each already found and
///     fixed for their own options
///     class. <see cref="TcpZoneTransferClient.PullAsync" />'s own <c>ConnectTimeout</c>/
///     <c>ReadTimeout</c> read the exact same <c>options.CurrentValue</c> pattern, so proving
///     <c>PrimaryPort</c> here (used by both methods) stands for the whole class.
/// </summary>
public sealed class TcpZoneTransferClientHotReloadTests
{
    private sealed class NoOpTsigService : ITsigService
    {
        public Task<DnsMessage> SignAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            CancellationToken stoppingToken = default
        ) => Task.FromResult(message);

        public Task<TsigVerificationResult> VerifyAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<DnsMessage> SignResponseAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<DnsMessage> SignContinuationAsync(
            DnsMessage message,
            string keyName,
            ushort originalId,
            byte[] priorMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<TsigVerificationResult> VerifyResponseAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] requestMac,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();

        public Task<TsigVerificationResult> VerifyContinuationAsync(
            byte[] rawMessage,
            int tsigRecordStartOffset,
            DnsMessage message,
            byte[] priorMac,
            IReadOnlyList<byte[]> unsignedMessageBytesSincePriorTsig,
            CancellationToken stoppingToken = default
        ) => throw new NotSupportedException();
    }

    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesWhichPortTheVeryNextSoaQueryTargets_WithNoRestart()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"ninlil-dns-zone-transfer-client-primary-port-hot-reload-{Guid.NewGuid():N}.json"
        );

        using UdpClient firstPrimary = new(new IPEndPoint(IPAddress.Loopback, 0));
        using UdpClient secondPrimary = new(new IPEndPoint(IPAddress.Loopback, 0));
        int firstPort = ((IPEndPoint)firstPrimary.Client.LocalEndPoint).Port;
        int secondPort = ((IPEndPoint)secondPrimary.Client.LocalEndPoint).Port;

        try
        {
            // A real IConfigurationRoot, backed by a real reload-on-change JSON file provider --
            // the exact same kind of source WebApplicationBuilder.CreateBuilder() wires up for
            // appsettings.json in a real deployment.
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile(path, optional: true, reloadOnChange: true)
                .Build();

            ServiceCollection services = new();
            services.Configure<ZoneTransferClientOptions>(
                configuration.GetSection(ZoneTransferClientOptions.SectionName)
            );
            await using ServiceProvider provider = services.BuildServiceProvider();

            IOptionsMonitor<ZoneTransferClientOptions> monitor = provider.GetRequiredService<
                IOptionsMonitor<ZoneTransferClientOptions>
            >();

            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        ZoneTransferClientOptions.SectionName,
                        nameof(ZoneTransferClientOptions.PrimaryPort),
                        firstPort
                    ),
                    new Server.Presets.PresetConfigurationSetting(
                        ZoneTransferClientOptions.SectionName,
                        nameof(ZoneTransferClientOptions.SoaQueryTimeout),
                        "00:00:05"
                    ),
                ],
                path
            );

            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.PrimaryPort == firstPort);

            TcpZoneTransferClient client = new(new NoOpTsigService(), monitor);
            DnsName zoneName = DnsName.Parse("example.test.");

            Task<uint?> firstQueryTask = client.QuerySoaSerialAsync(
                IPAddress.Loopback,
                zoneName,
                null
            );
            await RespondWithSoaAsync(firstPrimary, 42);
            Assert.Equal((uint)42, await firstQueryTask);

            // The exact same write mechanism ZoneTransferClientSettingsController.Patch itself
            // calls, this time pointing PrimaryPort at the second loopback listener instead.
            await Server.Presets.PresetConfigurationFileApplier.ApplyAsync(
                [
                    new Server.Presets.PresetConfigurationSetting(
                        ZoneTransferClientOptions.SectionName,
                        nameof(ZoneTransferClientOptions.PrimaryPort),
                        secondPort
                    ),
                ],
                path
            );

            // Polls the actual settled value directly rather than trusting a single OnChange
            // firing to mean the reload is fully applied -- a second write to the same monitor
            // within one test can otherwise race a stray/delayed notification left over from the
            // first write against this one, observed directly as a real, reproducible flake.
            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.PrimaryPort == secondPort);

            // The identical client instance, constructed before the write, now genuinely targets
            // the second listener -- proof the port was actually re-read rather than captured
            // once at construction.
            Task<uint?> secondQueryTask = client.QuerySoaSerialAsync(
                IPAddress.Loopback,
                zoneName,
                null
            );
            await RespondWithSoaAsync(secondPrimary, 43);
            Assert.Equal((uint)43, await secondQueryTask);
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
        IOptionsMonitor<ZoneTransferClientOptions> monitor,
        Func<bool> condition
    )
    {
        CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        while (!condition() && !cts.IsCancellationRequested)
            await Task.Delay(50);

        Assert.True(condition(), "The configuration reload did not happen within the timeout.");
    }

    /// <summary>
    ///     This method waits for an SOA query datagram on the given loopback listener and answers
    ///     it with a matching SOA record, the way a real primary would.
    /// </summary>
    private static async Task RespondWithSoaAsync(UdpClient listener, uint serial)
    {
        UdpReceiveResult received = await listener
            .ReceiveAsync()
            .WaitAsync(TimeSpan.FromSeconds(10));
        DnsMessage request = DnsMessageReader.Read(received.Buffer);
        DnsQuestion question = request.Questions[0];

        DnsMessage response = new()
        {
            Id = request.Id,
            IsResponse = true,
            Opcode = DnsOpcodeEnum.Query,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions = { question },
            Answers =
            {
                new DnsResourceRecord(
                    question.Name,
                    DnsResourceRecordTypeEnum.Soa,
                    DnsClassEnum.Internet,
                    3600,
                    DnsRecordDataCodecRegistry.Encode(
                        DnsResourceRecordTypeEnum.Soa,
                        new SoaRecordData(DnsName.Root, DnsName.Root, serial, 0, 0, 0, 0)
                    )
                ),
            },
        };

        byte[] responseBytes = DnsMessageWriter.Write(response);
        await listener.SendAsync(responseBytes, received.RemoteEndPoint);
    }
}
