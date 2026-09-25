using Ninlil.Dns.Server.Core.Events;

// Define our namespace.
namespace Ninlil.Dns.Server.AccessControl;

/// <summary>
///     This class keeps <see cref="DnsAccessControlRuleCache" /> in sync with the database.
///     When <c>DnsAccessControlRulesController</c> creates, updates or deletes a rule it publishes
///     a <see cref="DnsAccessControlRuleChangedEvent" />, and this subscriber reloads the whole
///     rule set (see <see cref="DnsAccessControlRuleCache" /> for why the whole set). It works like
///     <c>Ninlil.Dns.Server.Authoritative.Cache.ZoneChangedEventSubscriber</c>.
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
