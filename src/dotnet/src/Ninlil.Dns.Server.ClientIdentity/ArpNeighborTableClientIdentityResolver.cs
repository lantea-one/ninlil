using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Server.Core.ClientIdentity;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity;

/// <summary>
///     This class implements <see cref="IClientIdentityResolver" /> against this host's own real
///     OS-level ARP neighbor cache (Linux's <c>/proc/net/arp</c>) -- the mechanism that makes
///     "resolve a client's identity regardless of which DHCP server actually issued its lease"
///     genuinely true, since the kernel populates this table for any device that has ever
///     answered an ARP request on a locally-connected network, whether or not this server itself
///     ever handed that device a lease.
///     <para>
///         Mirrors <c>Ninlil.Dns.Server.AccessControl.DnsAccessControlRuleCache</c>'s own "load
///         once in <see cref="ExecuteAsync" />, keep an immutable in-memory snapshot" shape, but
///         reloads on a short fixed timer (<see cref="RefreshInterval" />) rather than an
///         event-bus invalidation -- there is no change event for "the kernel's ARP cache just
///         changed," so a poll is the only mechanism available, and ARP entries change slowly
///         enough (an entry lives for minutes at the kernel's own default) that a five-second
///         poll interval never meaningfully lags real device activity while still never
///         re-reading the file on every single query answered.
///     </para>
///     <para>
///         <strong>IPv4 only.</strong> The ARP protocol itself (RFC 826) only ever resolves IPv4
///         addresses; an IPv6 client's own hardware address is instead learned via Neighbor
///         Discovery (RFC 4861), a genuinely different kernel table
///         (<c>/proc/net/ipv6_neigh</c> or <c>ip -6 neigh</c>) this class does not read. A caller
///         resolving an IPv6 address always receives <see langword="null" /> from this resolver.
///     </para>
///     <para>
///         <strong>Environment-independence, honestly documented.</strong> A sandboxed,
///         containerized, or non-Linux environment may have no <c>/proc/net/arp</c> file at all
///         -- this class follows the same honest, non-crashing posture
///         <c>Ninlil.Dhcp.Protocol.Ra.Icmpv6RaListener</c>'s own raw-socket handling already
///         establishes for a comparable "this OS-level mechanism might not exist here" gap: the
///         absence is logged exactly once, and every subsequent resolution simply, permanently
///         answers <see langword="null" /> rather than throwing or retrying noisily forever.
///     </para>
/// </summary>
/// <param name="logger">The logger this resolver records its own one-time unavailability notice, and any unexpected read failure, to.</param>
public sealed class ArpNeighborTableClientIdentityResolver(
    ILogger<ArpNeighborTableClientIdentityResolver> logger
) : BackgroundService, IClientIdentityResolver
{
    /// <summary>
    ///     This constant contains the real, standard path the Linux kernel exposes its own ARP
    ///     neighbor table at.
    /// </summary>
    private const string ArpTablePath = "/proc/net/arp";

    /// <summary>
    ///     This constant contains how often this resolver re-reads the real ARP table -- a few
    ///     seconds, since an ARP entry changes slowly (see this class's own doc comment), so
    ///     re-reading the file on every single query answered would be pure waste.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    ///     This field contains the current, complete, immutable snapshot of every currently
    ///     resolved IPv4-address-to-hardware-address mapping. Replaced wholesale on every
    ///     refresh, never mutated in place, so a concurrent <see cref="ResolveAsync" /> call never
    ///     observes a half-built snapshot -- the exact same safety
    ///     <c>DnsAccessControlRuleCache</c>'s own <c>volatile</c> snapshot field already provides.
    /// </summary>
    private volatile IReadOnlyDictionary<IPAddress, string> _table =
        new Dictionary<IPAddress, string>();

    /// <summary>
    ///     This field remembers whether this resolver has already logged that
    ///     <see cref="ArpTablePath" /> is unavailable in this environment, so that condition is
    ///     ever only reported once per process rather than once per refresh tick forever.
    /// </summary>
    private bool _hasLoggedUnavailable;

    /// <inheritdoc />
    public Task<ClientIdentityResolution> ResolveAsync(
        IPAddress remoteAddress,
        CancellationToken stoppingToken = default
    )
    {
        // This project's own Plain DNS UDP listener is always a dual-stack socket bound to the
        // IPv6 wildcard address, so a real IPv4 client's own address arrives everywhere
        // downstream -- including here -- as an IPv4-mapped IPv6 address
        // (System.Net.Sockets.AddressFamily.InterNetworkV6, "::ffff:x.x.x.x"), never as a plain
        // AddressFamily.InterNetwork value. Unmapping it first is what makes both the family
        // check below and the dictionary lookup (keyed by the real, unmapped address this
        // table's own RefreshAsync parses out of /proc/net/arp) actually match a genuine,
        // real-world query instead of silently never resolving one at all.
        IPAddress normalizedAddress =
            remoteAddress?.IsIPv4MappedToIPv6 == true ? remoteAddress.MapToIPv4() : remoteAddress;

        // ARP only ever resolves an IPv4 address (see this class's own doc comment) -- an IPv6
        // lookup is answered null immediately, with no dictionary lookup at all.
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
        // Load once immediately, so the very first query answered right after startup already
        // has a chance at a real match rather than waiting a full RefreshInterval for the first
        // tick to fire.
        await RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(RefreshInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    /// <summary>
    ///     This method re-reads the real ARP table from disk and replaces this resolver's own
    ///     in-memory snapshot with what it found. Exposed as <see langword="public" /> (rather
    ///     than only ever running from <see cref="ExecuteAsync" />'s own timer loop) so a test can
    ///     force one real, synchronous read without waiting on <see cref="RefreshInterval" />.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task RefreshAsync(CancellationToken stoppingToken = default)
    {
        try
        {
            if (!File.Exists(ArpTablePath))
            {
                // Log this exactly once -- a sandboxed, containerized, or non-Linux environment
                // simply never has this file, and that is not a failure worth repeating on every
                // single refresh tick for the rest of this process's lifetime.
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
            // A real, requested shutdown -- let it propagate rather than logging it as a failure.
            throw;
        }
        catch (Exception exception)
        {
            // A transient read failure (a permissions change, the file disappearing mid-read)
            // must never crash this background service -- the next tick simply tries again, and
            // the previous snapshot keeps serving lookups in the meantime.
            logger.LogWarning(
                exception,
                "Failed to refresh the ARP neighbor table from '{ArpTablePath}'.",
                ArpTablePath
            );
        }
    }
}
