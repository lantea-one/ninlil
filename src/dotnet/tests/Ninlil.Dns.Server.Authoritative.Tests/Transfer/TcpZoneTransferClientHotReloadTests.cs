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
///     This class checks, with loopback UDP sockets, that a change to
///     <see cref="ZoneTransferClientOptions" /> written through
///     <see cref="Server.Presets.PresetConfigurationFileApplier" /> (what
///     <c>Ninlil.Web.Controllers.ZoneTransferClientSettingsController.Patch</c> calls) into a
///     reload-on-change JSON file changes the port the next
///     <see cref="TcpZoneTransferClient.QuerySoaSerialAsync" /> uses, without a restart.
///     <see cref="TcpZoneTransferClient.PullAsync" /> reads its port and timeouts the same way, so
///     this one check stands for both methods. An <see cref="IOptions{TOptions}" /> value would be
///     cached for the life of the process.
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
            // A reload-on-change JSON file source, the same kind the web host uses for its
            // configuration files.
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

            // The same write ZoneTransferClientSettingsController.Patch makes, pointing PrimaryPort at
            // the second listener.
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

            // Poll the value itself rather than trusting one OnChange: a late notification from the
            // first write can fire first and made this test flaky.
            await WaitForReloadAsync(monitor, () => monitor.CurrentValue.PrimaryPort == secondPort);

            // The same client, built before the write, now uses the second listener, so the port is
            // re-read per call.
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
                // Best-effort cleanup; a leftover temp file is harmless.
            }
        }
    }

    /// <summary>
    ///     The JSON provider debounces file-change notifications, so wait for the reload event
    ///     rather than a fixed delay.
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
