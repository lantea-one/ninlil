// Define our namespace.
namespace Ninlil.Dns.Server.Core.Events;

/// <summary>
///     This event is published whenever an access-control rule changes -- created,
///     updated, deleted, or enabled/disabled through <c>DnsAccessControlRulesController</c>.
///     <c>Ninlil.Dns.Server.AccessControl.DnsAccessControlRuleChangedEventSubscriber</c>
///     reacts to this by reloading its cache's entire rule set, the same
///     <see cref="DnsZoneChangedEvent" />-driven invalidation shape
///     <c>AuthoritativeZoneCache</c> already establishes -- adapted here to a whole-set reload
///     since an access-control rule table is small and homelab-scale, unlike a zone's own
///     potentially-thousands-of-records content.
/// </summary>
/// <param name="OccurredAtUtc">The UTC date and time the rule changed.</param>
/// <param name="RuleId">The identifier of the rule that changed.</param>
/// <param name="IsDeleted">
///     Whether this change was a deletion -- <see langword="true" /> only for
///     <c>DnsAccessControlRulesController.Delete</c>. Carried for the cluster's own config sync,
///     whose own subscriber (unlike <c>DnsAccessControlRuleChangedEventSubscriber</c>'s
///     whole-set cache reload, which does not care whether a rule was deleted or merely changed)
///     needs to know which kind of change to forward to every peer.
/// </param>
public record DnsAccessControlRuleChangedEvent(
    DateTimeOffset OccurredAtUtc,
    Guid RuleId,
    bool IsDeleted = false
) : IDnsServerEvent;
