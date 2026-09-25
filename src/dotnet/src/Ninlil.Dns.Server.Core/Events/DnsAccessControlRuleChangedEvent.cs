// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published when an access-control rule is created, updated, deleted, enabled
///     or disabled through <c>DnsAccessControlRulesController</c>.
///     <c>DnsAccessControlRuleChangedEventSubscriber</c> (Server.AccessControl) reloads the whole
///     rule cache, which is cheap because the rule table is small, and cluster sync forwards the
///     change to peers.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the rule changed.</param>
/// <param name="RuleId">The identifier of the rule that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion (<see langword="true" /> only for
///     <c>DnsAccessControlRulesController.Delete</c>). Cluster sync needs it to tell peers to
///     delete rather than update; the cache reload ignores it.
/// </param>
public record DnsAccessControlRuleChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid RuleId,
    bool IsDeleted = false
) : IDnsServerEvent;
