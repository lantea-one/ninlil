using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> from the host's ARP table
///     (Linux's <c>/proc/net/arp</c>). The kernel fills that table for every device that has
///     answered an ARP request on a directly connected network, so this resolver identifies a
///     client whichever DHCP server gave it its lease. <c>Ninlil.Web/Program.cs</c> puts it last in
///     the <see cref="CompositeClientIdentityResolver" /> chain.
///     <para>
///         It keeps an in-memory snapshot, loaded in <see cref="ExecuteAsync" /> and reloaded every
///         <see cref="RefreshInterval" />. The kernel publishes no change event for its ARP table,
///         so polling is the only option; entries live for minutes, so a five-second poll keeps up
///         without reading the file on every query.
///     </para>
///     <para>
///         <strong>IPv4 only.</strong> ARP (RFC 826) resolves IPv4 addresses only. IPv6 hardware
///         addresses come from Neighbor Discovery (RFC 4861), a different kernel table this class
///         does not read, so an IPv6 address always resolves to <see langword="null" />.
///     </para>
///     <para>
///         <strong>Missing table.</strong> A sandboxed, containerized or non-Linux host may have
///         no <c>/proc/net/arp</c>. That is logged once, and every lookup then answers
///         <see langword="null" /> rather than throwing. The file is still checked on every
///         refresh, so it is picked up if it appears later.
///     </para>
/// </summary>
/// <param name="logger">The logger used for the one-time unavailability notice and read failures.</param>
public sealed class ArpNeighborTableClientIdentityResolver(
    ILogger<ArpNeighborTableClientIdentityResolver> logger
) : BackgroundService, IClientIdentityResolver
{
    /// <summary>
    ///     This constant contains the path of the Linux kernel's ARP table.
    /// </summary>
    private const string ArpTablePath = "/proc/net/arp";

    /// <summary>
    ///     This field contains how often the ARP table is re-read (see the class summary).
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This field contains the current snapshot of IPv4-address-to-hardware-address mappings.
    ///     Each refresh replaces the whole dictionary and never mutates it, so a concurrent
    ///     <see cref="ResolveAsync" /> never sees a half-built snapshot.
    /// </summary>
    private volatile IReadOnlyDictionary<IPAddress, string> _table =
        new Dictionary<IPAddress, string>();

    /// <summary>
    ///     This field records whether the missing <see cref="ArpTablePath" /> has been logged, so
    ///     it is reported once per process rather than on every refresh.
    /// </summary>
    private bool _hasLoggedUnavailable;

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    )
    {
        // With no listen addresses configured, the listeners use one dual-stack socket, so an
        // IPv4 client arrives as an IPv4-mapped IPv6 address ("::ffff:x.x.x.x"). Unmap it so the
        // family check and the lookup (keyed by the plain IPv4 addresses in /proc/net/arp) match.
        IPAddress normalizedAddress =
            remoteAddress?.IsIPv4MappedToIPv6 == true ? remoteAddress.MapToIPv4() : remoteAddress;

        // ARP resolves only IPv4 addresses, so an IPv6 address is answered null at once.
        if (
            normalizedAddress != null
            && normalizedAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && _table.TryGetValue(normalizedAddress, out string hardwareAddress)
        )
        {
            return Task.FromResult(
                new ClientIdentityResolution(hardwareAddress, InternetProtocolVersionEnum.IPv4)
            );
        }

        return Task.FromResult<ClientIdentityResolution>(null);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Load once at startup, so early queries can match without waiting a RefreshInterval.
        await RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    /// <summary>
    ///     This method re-reads the ARP table and replaces the in-memory snapshot. It is public so
    ///     a caller can force a read without waiting for <see cref="RefreshInterval" />. A read
    ///     failure is logged and the previous snapshot kept.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task RefreshAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            if (!File.Exists(ArpTablePath))
            {
                // Log this once: some environments never have the file, and repeating it on every
                // refresh would add nothing.
                if (!_hasLoggedUnavailable)
                {
                    logger.LogInformation(
                        "'{ArpTablePath}' does not exist in this environment -- ARP-based "
                            + "client identity resolution is disabled for this process. This is "
                            + "expected on a non-Linux host, or inside a sandboxed/restricted "
                            + "container without access to the host's own network stack.",
                        ArpTablePath
                    );
                    _hasLoggedUnavailable = true;
                }

                return;
            }

            string contents = await File.ReadAllTextAsync(ArpTablePath, stoppingToken);
            IReadOnlyList<ArpNeighborTableEntry> entries = ArpNeighborTableParser.Parse(contents);

            Dictionary<IPAddress, string> table = new(entries.Count);

            foreach (ArpNeighborTableEntry entry in entries)
                table[entry.IpAddress] = entry.HardwareAddress;

            _table = table;
        }
        catch (OperationCanceledException)
        {
            // A requested shutdown: let it propagate rather than logging it as a failure.
            throw;
        }
        catch (Exception exception)
        {
            // A read failure (a permissions change, the file vanishing mid-read) must not stop the
            // service: the next tick tries again, and the previous snapshot keeps serving lookups.
            logger.LogWarning(
                exception,
                "Failed to refresh the ARP neighbor table from '{ArpTablePath}'.",
                ArpTablePath
            );
        }
    }
}
