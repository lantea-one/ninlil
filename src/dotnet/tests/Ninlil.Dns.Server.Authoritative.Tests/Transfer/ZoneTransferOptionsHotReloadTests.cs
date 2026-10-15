using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Authoritative.Options;
using Ninlil.Dns.Server.Presets;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class checks that a change to <see cref="ZoneTransferOptions.RecordsPerMessage" />,
///     written through <see cref="PresetConfigurationFileApplier" /> (what the zone transfer
///     settings controller calls) into a reload-on-change JSON file, changes how many messages the
///     next AXFR of <see cref="Authoritative.Transfer.ZoneTransferService" /> uses, without a
///     restart. It runs the real service (scoped, so each transfer gets a new instance that reads
///     the singleton options monitor) in a <see cref="TsigTransferHost" /> (SQLite, a real TSIG
///     service, no sockets) whose configuration also holds the file, and counts the messages of a
///     signed AXFR of the six-record zone.
/// </summary>
public sealed class ZoneTransferOptionsHotReloadTests
{
    [Fact]
    public async Task AWriteThroughPresetConfigurationFileApplier_ChangesTheMessageCountOfTheNextAxfr_WithNoRestart()
    {
        // A directory for this test rather than the shared temp directory: the file watcher
        // behind reloadOnChange sees every change in the directory it watches, and unrelated
        // churn there can make it report a change to every watched file.
        string directory = Directory
            .CreateTempSubdirectory("ninlil-dns-zone-transfer-hot-reload-")
            .FullName;
        string path = Path.Combine(directory, "appsettings.json");

        try
        {
            await using TsigTransferHost host = await TsigTransferHost.CreateAsync(
                recordsPerMessage: 2,
                reloadableConfigurationPath: path
            );
            string key = await host.AddKeyAsync();

            // Six records (SOA, five A records) at two per message; the closing SOA joins the last
            // message.
            Assert.Equal(3, await CountAxfrMessagesAsync(host, key));

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        ZoneTransferOptions.SectionName,
                        nameof(ZoneTransferOptions.RecordsPerMessage),
                        100
                    ),
                ],
                path
            );

            // The JSON provider debounces file-change notifications. Poll the effect itself, up
            // to ten seconds, instead of sleeping for a fixed time: a spurious earlier
            // notification can reload the file before the write lands.
            int messageCount = 3;
            for (int attempt = 0; attempt < 200 && messageCount != 1; attempt++)
            {
                await Task.Delay(50);
                messageCount = await CountAxfrMessagesAsync(host, key);
            }

            Assert.Equal(1, messageCount);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary file is harmless.
            }
        }
    }

    /// <summary>
    ///     This method runs a signed AXFR of the host's zone and returns how many messages the
    ///     transfer service yields.
    /// </summary>
    private static async Task<int> CountAxfrMessagesAsync(TsigTransferHost host, string keyName)
    {
        (DnsMessage request, byte[] raw, int offset) = await host.SignedQueryAsync(
            TsigTransferHost.ZoneName,
            DnsResourceRecordTypeEnum.Axfr,
            keyName
        );

        List<DnsMessage> messages = await host.TransferAsync(request, raw, offset);

        Assert.All(
            messages,
            message => Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode)
        );
        return messages.Count;
    }
}
