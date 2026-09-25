using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.ClientIdentity.Presence.Netlink;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Presence;

/// <summary>
///     This background service samples the kernel neighbor table on
///     <see cref="ClientPresenceOptions.NeighborTablePollInterval" /> and records a hardware-address
///     sighting for every entry the kernel currently holds as confirmed reachable. This signal
///     covers a device that neither leases an address from this server nor sends it DNS queries.
///     It is passive: it only reads the table the kernel maintains and sends nothing on the
///     network.
///     <para>
///         Sampling is skipped while <see cref="ClientPresenceOptions.EnableNeighborTableSignal" />
///         is off, re-checked every interval so a settings change applies live. When the table
///         cannot be read on this platform at all, this is logged once and the service keeps idling
///         quietly rather than failing host startup.
///     </para>
/// </summary>
/// <param name="reader">The neighbor-table reader.</param>
/// <param name="recorder">The recorder sightings are handed to.</param>
/// <param name="options">The live presence options.</param>
/// <param name="logger">The logger an unsupported platform is reported to.</param>
public sealed class NeighborTablePresenceService(
    INeighborTableReader reader,
    IClientPresenceRecorder recorder,
    IOptionsMonitor<ClientPresenceOptions> options,
    ILogger<NeighborTablePresenceService> logger
) : BackgroundService
{
    /// <summary>
    ///     This field records whether the unsupported-platform notice was already logged.
    /// </summary>
    private bool _reportedUnsupported;

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (options.CurrentValue.EnableNeighborTableSignal)
                SampleOnce();

            try
            {
                await Task.Delay(options.CurrentValue.NeighborTablePollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    ///     This method reads the neighbor table once and records every confirmed-reachable entry
    ///     with an Ethernet-length link-layer address.
    /// </summary>
    /// <returns>The number of sightings recorded.</returns>
    public int SampleOnce()
    {
        IReadOnlyList<NeighborTableEntry> entries;
        try
        {
            entries = reader.Read();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the kernel neighbor table.");
            return 0;
        }

        if (entries is null)
        {
            if (!_reportedUnsupported)
            {
                _reportedUnsupported = true;
                logger.LogInformation(
                    "The kernel neighbor table cannot be read on this platform; client presence will rely on DHCP and DNS activity only."
                );
            }
            return 0;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        int recorded = 0;
        foreach (NeighborTableEntry entry in entries)
        {
            if (!entry.IsConfirmedReachable || entry.LinkLayerAddress is not { Length: 6 })
                continue;

            recorder.RecordHardwareAddress(entry.LinkLayerAddress, entry.Address, now);
            recorded++;
        }

        return recorded;
    }
}
