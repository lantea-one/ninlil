using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class holds every currently-enabled <see cref="DnsAccessControlRuleEntity" />, in
///     memory, as a plain snapshot list -- the one thing standing between
///     <see cref="DnsAccessControlEngine" /> and a database round trip on every single capability
///     check it answers on this project's hot query-answering path.
///     <para>
///         This mirrors <c>Ninlil.Dns.Server.Authoritative.Cache.AuthoritativeZoneCache</c>'s
///         own "load once in <see cref="StartAsync" />, invalidate via the event bus" shape --
///         confirmed by Phase 5o-10a's own design record to be the real, working precedent for
///         this pattern in this codebase, not <c>DnsFilterEngine</c> (which, read directly, opens
///         a fresh repository scope on every single call and caches nothing about client
///         groups/networks at all). Unlike <c>AuthoritativeZoneCache</c>, which reloads a single
///         changed zone in isolation, this cache always reloads its <em>entire</em> snapshot on
///         invalidation -- a deliberate difference: an access-control rule table is small and
///         flat, homelab-scale (the same assumption <c>Ninlil.Dns.Server.RateLimiting.TokenBucketDnsRateLimiter</c>'s
///         own doc comment already makes about this deployment target), so a full reload is
///         simpler and cheap enough not to need row-level invalidation the way a zone's own
///         potentially-thousands-of-records content would.
///     </para>
/// </summary>
/// <param name="scopeFactory">
///     The factory used to reach <see cref="IDnsAccessControlRuleRepository" /> in a scope of its
///     own, so this cache never holds a database context open longer than a single load.
/// </param>
public sealed class DnsAccessControlRuleCache(IServiceScopeFactory scopeFactory) : IHostedService
{
    /// <summary>
    ///     This field contains the current, complete, immutable snapshot of every enabled rule.
    ///     Replaced wholesale, never mutated in place, so a concurrent reader never observes a
    ///     half-built snapshot -- the same "swap a whole new immutable value in" safety a
    ///     <c>ConcurrentDictionary</c> gives <c>AuthoritativeZoneCache</c> per-key, applied here at
    ///     the whole-list granularity this cache actually reloads at.
    /// </summary>
    private volatile IReadOnlyList<DnsAccessControlRuleEntity> _rules = [];

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken) =>
        await ReloadAsync(cancellationToken);

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method checks whether any currently-cached, enabled rule grants a specific
    ///     capability to an address -- answered entirely from the in-memory snapshot
    ///     <see cref="_rules" /> already holds, with zero synchronous database I/O.
    /// </summary>
    /// <param name="remoteAddress">The querying client's own source address.</param>
    /// <param name="capability">The capability being checked for.</param>
    /// <returns><see langword="true" /> if at least one cached rule grants it.</returns>
    public bool HasCapability(IPAddress remoteAddress, DnsAccessControlCapabilityEnum capability)
    {
        // Capture the current snapshot reference once -- ReloadAsync may swap _rules to a new
        // list concurrently, but this method must only ever see one consistent list for the
        // duration of this single check, never a mix of the old and new.
        IReadOnlyList<DnsAccessControlRuleEntity> rules = _rules;

        foreach (DnsAccessControlRuleEntity rule in rules)
        {
            if (!rule.Capabilities.HasFlag(capability))
                continue;

            if (NetworkContains(rule.Cidr, remoteAddress))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     This method reloads this cache's entire snapshot from the database, replacing whatever
    ///     it currently holds -- the only reload shape this cache supports (see this class's own
    ///     doc comment for why a whole-set reload, rather than a single-rule reload, is the right
    ///     tradeoff here).
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    public async Task ReloadAsync(CancellationToken stoppingToken = default)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IDnsAccessControlRuleRepository repository =
            scope.ServiceProvider.GetRequiredService<IDnsAccessControlRuleRepository>();

        _rules = await repository.ListEnabledAsync(stoppingToken);
    }

    /// <summary>
    ///     This method checks whether a network entry (a bare address or a CIDR network) contains
    ///     a single address -- the same tolerant parsing <c>DnsFilterEngine.NetworkContains</c>
    ///     already applies to a comparably free-text network field, even though
    ///     <see cref="DnsAccessControlRuleEntity.Cidr" />'s own doc comment expects every stored
    ///     value to already be in <see cref="IPNetwork.Parse(string)" />-compatible form.
    /// </summary>
    private static bool NetworkContains(string cidr, IPAddress address)
    {
        if (string.IsNullOrWhiteSpace(cidr))
            return false;

        try
        {
            return cidr.Contains('/')
                ? IPNetwork.Parse(cidr).Contains(address)
                : IPAddress.Parse(cidr).Equals(address);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
