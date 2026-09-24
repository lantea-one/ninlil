using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class keeps <see cref="DnsAccessControlRuleCache" /> in sync with the database:
///     whenever something -- the future <c>AccessControlRulesController</c> (Phase 5o-10c) --
///     publishes a <see cref="DnsAccessControlRuleChangedEvent" />, this reloads the cache's
///     entire rule set. Mirrors <c>Ninlil.Dns.Server.Authoritative.Cache.ZoneChangedEventSubscriber</c>'s
///     exact shape, adapted to a whole-set reload rather than reloading just the one rule that
///     changed -- see <see cref="DnsAccessControlRuleCache" />'s own doc comment for why.
/// </summary>
/// <param name="cache">The cache to keep in sync.</param>
public class DnsAccessControlRuleChangedEventSubscriber(DnsAccessControlRuleCache cache)
    : IDnsServerEventSubscriber<DnsAccessControlRuleChangedEvent>
{
    /// <inheritdoc />
    public ValueTask HandleAsync(
        DnsAccessControlRuleChangedEvent domainEvent,
        CancellationToken stoppingToken
    ) => new(cache.ReloadAsync(stoppingToken));
}
